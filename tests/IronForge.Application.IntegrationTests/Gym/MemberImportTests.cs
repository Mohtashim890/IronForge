using IronForge.Application.Entities;
using IronForge.Application.Gym.Common;
using IronForge.Application.IntegrationTests.Infrastructure;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Gym;
using IronForge.Shared.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace IronForge.Application.IntegrationTests.Gym;

/// <summary>
/// Bulk member onboarding from a CSV file against PostgreSQL: preview saves
/// nothing, import creates only valid rows, staff are never imported,
/// billing starts after the paid period, and gyms stay apart.
/// </summary>
public sealed class MemberImportTests
    : IntegrationTestBase,
      IClassFixture<PostgreSqlTestDatabase>
{
    private static readonly DateTimeOffset Start = new(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly TestClock _clock = new(Start);

    public MemberImportTests(PostgreSqlTestDatabase database) : base(database)
    {
    }

    private DateOnly Today => DateOnly.FromDateTime(_clock.Now.UtcDateTime);

    [Fact]
    public async Task Preview_ChecksEveryRow_AndSavesNothing()
    {
        var gym = await SeedGymAsync();

        var preview = await AsAdmin(gym, s => s.MemberImport.PreviewAsync(new MemberImportRequest
        {
            Csv = "FullName,Email,Plan,PaidThrough\n" +
                  "Sara Khan,Sara.Khan@Example.com,monthly,2026-04-30\n" +
                  "Omar Ali,omar@example.com,Gold,\n" +
                  ",not-an-email,Monthly,30/04/2026\n" +
                  "Sara Again,sara.khan@example.com,Monthly,\n"
        }));

        Assert.True(preview.Success, preview.ErrorMessage);
        var result = preview.Data!;

        Assert.False(result.Imported);
        Assert.Equal(1, result.ReadyCount);
        Assert.Equal(3, result.ErrorCount);

        var sara = result.Rows.Single(x => x.Line == 2);
        Assert.Equal("sara.khan@example.com", sara.Email);
        Assert.Equal(new DateOnly(2026, 5, 1), sara.FirstBillingDate);

        Assert.Contains("no active plan \"Gold\"", result.Rows.Single(x => x.Line == 3).Message);
        Assert.Contains("FullName is empty", result.Rows.Single(x => x.Line == 4).Message);
        Assert.Contains("not an email address", result.Rows.Single(x => x.Line == 4).Message);
        Assert.Contains("not a date", result.Rows.Single(x => x.Line == 4).Message);
        Assert.Contains("already on line 2", result.Rows.Single(x => x.Line == 5).Message);

        await using var db = Database.CreateDbContext();
        Assert.False(await db.MemberProfiles.IgnoreQueryFilters().AnyAsync(x => x.TenantId == gym.TenantId));
    }

    [Fact]
    public async Task Import_CreatesActiveMembers_BilledFromTheDayAfterTheirPaidPeriod()
    {
        var gym = await SeedGymAsync();

        var imported = await AsAdmin(gym, s => s.MemberImport.ImportAsync(new MemberImportRequest
        {
            Csv = "﻿Full Name;email;PLAN;Paid_Through;MemberSince;Gender;Phone\r\n" +
                  "Sara Khan;sara@example.com;Monthly;2026-04-30;2023-04-01;female;\"+92 300; ext 2\"\r\n" +
                  "Omar Ali;omar@example.com;Monthly;2025-12-31;;;\r\n" +
                  "Bad Row;bad@example.com;Monthly;;;Robot;\r\n" +
                  ";;;;;;\r\n"
        }));

        Assert.True(imported.Success, imported.ErrorMessage);
        Assert.True(imported.Data!.Imported);
        Assert.Equal(2, imported.Data.ReadyCount);
        Assert.Equal(1, imported.Data.ErrorCount);

        await using var db = Database.CreateDbContext();
        var members = await db.MemberProfiles.IgnoreQueryFilters().Where(x => x.TenantId == gym.TenantId).ToListAsync();

        var sara = members.Single(x => x.Email == "sara@example.com");
        Assert.Equal(MemberStatus.Active, sara.Status);
        Assert.NotNull(sara.ApprovedAtUtc);
        Assert.Equal(new DateOnly(2026, 4, 30), sara.PaidThrough);
        Assert.Equal(new DateOnly(2026, 5, 1), sara.NextBillingDate);
        Assert.Equal("Female", sara.Gender);
        Assert.Equal("+92 300; ext 2", sara.Phone);
        Assert.Equal(new DateTime(2023, 4, 1), sara.JoinedAtUtc);

        // Paid period already over: billed from today, not for the months before the import.
        var omar = members.Single(x => x.Email == "omar@example.com");
        Assert.Null(omar.PaidThrough);
        Assert.Equal(Today, omar.NextBillingDate);

        // The import itself issues no invoices and sends nothing.
        Assert.False(await db.Invoices.IgnoreQueryFilters().AnyAsync(x => x.TenantId == gym.TenantId));
        Assert.False(await db.EmailOutbox.AnyAsync());
        Assert.True(await db.AuditEvents.IgnoreQueryFilters().AnyAsync(x => x.Action == "MembersImported"));
    }

    [Fact]
    public async Task ExistingMembersAreSkipped_StaffAreRefused_AndAccountsAreLinked()
    {
        var gym = await SeedGymAsync();
        await SeedUserAsync(gym.TenantId, "trainer@gym.test", TenantRoles.Trainer);
        var appUser = await SeedUserAsync(null, "has.app@example.com", null);

        await AsAdmin(gym, s => s.MemberImport.ImportAsync(new MemberImportRequest
        {
            Csv = "FullName,Email,Plan\nSara Khan,sara@example.com,Monthly\n"
        }));

        var result = (await AsAdmin(gym, s => s.MemberImport.ImportAsync(new MemberImportRequest
        {
            Csv = "FullName,Email,Plan\n" +
                  "Sara Khan,sara@example.com,Monthly\n" +
                  "A Trainer,trainer@gym.test,Monthly\n" +
                  "Has App,has.app@example.com,Monthly\n",
            SendInvitations = true
        }))).Data!;

        Assert.Equal(MemberImportRowStatus.Skipped, result.Rows.Single(x => x.Line == 2).Status);
        Assert.Contains("staff member", result.Rows.Single(x => x.Line == 3).Message);
        Assert.Equal(MemberImportRowStatus.Ready, result.Rows.Single(x => x.Line == 4).Status);
        Assert.Equal(1, result.LinkedToAccounts);
        Assert.Equal(0, result.InvitationsSent);

        await using var db = Database.CreateDbContext();
        Assert.False(await db.MemberProfiles.IgnoreQueryFilters().AnyAsync(x => x.Email == "trainer@gym.test"));
        Assert.True(await db.MemberProfiles.IgnoreQueryFilters().AnyAsync(x => x.Email == "has.app@example.com" && x.UserId == appUser));
        Assert.True(await db.TenantMemberships.AnyAsync(x =>
            x.TenantId == gym.TenantId && x.UserId == appUser && x.Role == TenantRoles.Member && x.IsActive));
    }

    [Fact]
    public async Task Invitations_AreEmailed_WhenAsked()
    {
        var gym = await SeedGymAsync();

        var result = (await AsAdmin(gym, s => s.MemberImport.ImportAsync(new MemberImportRequest
        {
            Csv = "FullName,Email,Plan\nSara Khan,sara@example.com,Monthly\nOmar Ali,omar@example.com,Monthly\n",
            SendInvitations = true
        }))).Data!;

        Assert.Equal(2, result.InvitationsSent);

        await using var db = Database.CreateDbContext();
        Assert.Equal(2, await db.EmailOutbox.CountAsync(x => x.ToEmail == "sara@example.com" || x.ToEmail == "omar@example.com"));
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("Name,Email,Plan\nSara,sara@example.com,Monthly\n", "Unknown column \"Name\"")]
    [InlineData("FullName,Email\nSara,sara@example.com\n", "must have the columns Plan")]
    [InlineData("FullName,Email,Plan,Email\n", "appears twice")]
    [InlineData("FullName,Email,Plan\n", "no members")]
    public async Task BadFiles_AreRefusedAsAWhole(string csv, string error)
    {
        var gym = await SeedGymAsync();

        var result = await AsAdmin(gym, s => s.MemberImport.PreviewAsync(new MemberImportRequest { Csv = csv }));

        Assert.Contains(error, result.Data!.FileError);
    }

    [Fact]
    public async Task OnlyMemberManagers_CanImport_AndAPlanIsNeeded()
    {
        var gym = await SeedGymAsync();
        var trainer = await SeedUserAsync(gym.TenantId, "trainer@gym.test", TenantRoles.Trainer);

        var byTrainer = await AsUser(gym, trainer!.Value, TenantRoles.Trainer, s => s.MemberImport.PreviewAsync(
            new MemberImportRequest { Csv = "FullName,Email,Plan\nSara,sara@example.com,Monthly\n" }));
        Assert.Equal(403, byTrainer.StatusCode);

        var noPlans = await SeedGymAsync(withPlan: false);
        var withoutPlan = await AsAdmin(noPlans, s => s.MemberImport.PreviewAsync(
            new MemberImportRequest { Csv = "FullName,Email,Plan\nSara,sara@example.com,Monthly\n" }));
        Assert.Equal(400, withoutPlan.StatusCode);
    }

    [Fact]
    public async Task SameEmailInAnotherGym_IsANewMemberHere()
    {
        var gymA = await SeedGymAsync();
        var gymB = await SeedGymAsync();
        var csv = "FullName,Email,Plan\nSara Khan,sara@example.com,Monthly\n";

        await AsAdmin(gymA, s => s.MemberImport.ImportAsync(new MemberImportRequest { Csv = csv }));
        var inB = (await AsAdmin(gymB, s => s.MemberImport.PreviewAsync(new MemberImportRequest { Csv = csv }))).Data!;

        Assert.Equal(MemberImportRowStatus.Ready, inB.Rows.Single().Status);
    }

    // ---- Helpers ------------------------------------------------------------------

    private sealed record SeededGym(int TenantId, int AdminUserId);

    private async Task<SeededGym> SeedGymAsync(bool withPlan = true)
    {
        await using var db = Database.CreateDbContext();

        var tenant = new Tenant
        {
            Name = "Gym",
            Slug = $"gym-{Guid.NewGuid():N}",
            IsActive = true,
            CreatedAtUtc = Start.UtcDateTime,
            Plan = SubscriptionPlans.Professional.Id
        };

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        db.GymSettings.Add(new GymSettings
        {
            TenantId = tenant.Id,
            JoinCode = GymCalendar.NewJoinCode(),
            Currency = "USD",
            TimeZoneId = "UTC",
            CreatedAtUtc = Start.UtcDateTime,
            UpdatedAtUtc = Start.UtcDateTime
        });

        if (withPlan)
        {
            db.MembershipPlans.Add(new MembershipPlan
            {
                TenantId = tenant.Id,
                Name = "Monthly",
                Price = 50m,
                BillingInterval = BillingInterval.Monthly,
                CreatedAtUtc = Start.UtcDateTime
            });
        }

        await db.SaveChangesAsync();

        var admin = await SeedUserAsync(tenant.Id, $"admin{tenant.Id}@gym.test", TenantRoles.Admin);

        return new SeededGym(tenant.Id, admin!.Value);
    }

    /// <summary>An account, with a role in the gym when <paramref name="role"/> is given.</summary>
    private async Task<int?> SeedUserAsync(int? tenantId, string email, string? role)
    {
        await using var db = Database.CreateDbContext();

        var user = new User
        {
            Username = email.Replace("@", "_") + Guid.NewGuid().ToString("N")[..6],
            Email = email,
            Role = PlatformRoles.User,
            IsActive = true,
            CreatedAtUtc = Start.UtcDateTime
        };

        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "Password123!");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        if (tenantId is { } id && role is not null)
        {
            db.TenantMemberships.Add(new TenantMembership
            {
                TenantId = id,
                UserId = user.Id,
                Role = role,
                IsActive = true,
                CreatedAtUtc = Start.UtcDateTime
            });

            await db.SaveChangesAsync();
        }

        return user.Id;
    }

    private Task<T> AsAdmin<T>(SeededGym gym, Func<TestServiceFactory, Task<T>> action) =>
        AsUser(gym, gym.AdminUserId, TenantRoles.Admin, action);

    private async Task<T> AsUser<T>(SeededGym gym, int userId, string role, Func<TestServiceFactory, Task<T>> action)
    {
        var context = TestServiceFactory.AsRole(gym.TenantId, userId, role);
        await using var db = Database.CreateDbContext(context);

        return await action(new TestServiceFactory(db, context) { Clock = _clock });
    }
}
