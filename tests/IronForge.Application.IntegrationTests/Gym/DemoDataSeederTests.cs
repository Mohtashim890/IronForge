using IronForge.Application.Auth.DTOs;
using IronForge.Application.Entities;
using IronForge.Application.IntegrationTests.Infrastructure;
using IronForge.Infrastructure.Data.Seeding;
using IronForge.Shared.Models.Gym;
using IronForge.Shared.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace IronForge.Application.IntegrationTests.Gym;

public sealed class DemoDataSeederTests
    : IntegrationTestBase,
      IClassFixture<PostgreSqlTestDatabase>
{
    public DemoDataSeederTests(PostgreSqlTestDatabase database) : base(database)
    {
    }

    [Fact]
    public async Task Seed_CreatesTheDemoPlatform_AndIsIdempotent()
    {
        await using (var db = Database.CreateDbContext())
        {
            var seeded = await new DemoDataSeeder(db, new PasswordHasher<User>(), NullLogger.Instance).SeedAsync();
            Assert.True(seeded);
        }

        await using (var db = Database.CreateDbContext())
        {
            var again = await new DemoDataSeeder(db, new PasswordHasher<User>(), NullLogger.Instance).SeedAsync();
            Assert.False(again);
        }

        await using var check = Database.CreateDbContext();

        var tenants = await check.Tenants.OrderBy(x => x.Name).ToListAsync();

        Assert.Equal(
            ["FlexZone Fitness", "Iron Paradise Gym", "Old Barn Gym", "Titan Strength Club"],
            tenants.Select(x => x.Name));
        Assert.Contains(tenants, x => x.SubscriptionStatus == SubscriptionStatus.PastDue);
        Assert.Contains(tenants, x => x.SubscriptionStatus == SubscriptionStatus.Canceled);
        Assert.Contains(tenants, x => x.SubscriptionStatus == SubscriptionStatus.Trial);

        Assert.Equal(80, await check.MemberProfiles.IgnoreQueryFilters().CountAsync());
        Assert.True(await check.MemberProfiles.IgnoreQueryFilters().AnyAsync(x => x.Status == MemberStatus.Pending));
        Assert.True(await check.Invoices.IgnoreQueryFilters().AnyAsync(x => x.Status == InvoiceStatus.Overdue));
        Assert.True(await check.CheckIns.IgnoreQueryFilters().CountAsync() > 500);

        // Each gym's invoice counter continues after the seeded invoices.
        foreach (var settings in await check.GymSettings.ToListAsync())
        {
            var count = await check.Invoices.IgnoreQueryFilters().CountAsync(x => x.TenantId == settings.TenantId);
            Assert.Equal(count, settings.LastInvoiceNumber);
        }

        // The Starter gym has no coaching data and no coach agent.
        var flexZone = tenants.Single(x => x.Slug == "flexzone");
        Assert.False(await check.WorkoutRoutines.IgnoreQueryFilters().AnyAsync(x => x.TenantId == flexZone.Id));
        Assert.Equal(["gym-admin"], await check.Agents.IgnoreQueryFilters().Where(x => x.TenantId == flexZone.Id).Select(x => x.AgentId).ToListAsync());

        // The showcase member has a rich history.
        var demo = await check.MemberProfiles.IgnoreQueryFilters().SingleAsync(x => x.Email == "member@ironparadise.demo");
        Assert.Equal(MemberStatus.Active, demo.Status);
        Assert.True(await check.WorkoutLogs.IgnoreQueryFilters().CountAsync(x => x.MemberProfileId == demo.Id) > 20);
        Assert.True(await check.BodyMetrics.IgnoreQueryFilters().CountAsync(x => x.MemberProfileId == demo.Id) >= 12);
        Assert.True(await check.Invoices.IgnoreQueryFilters().AnyAsync(x => x.MemberProfileId == demo.Id && x.Status == InvoiceStatus.Open));
    }

    [Theory]
    [InlineData("operator@ironforge.dev")]
    [InlineData("admin@ironparadise.demo")]
    [InlineData("member@ironparadise.demo")]
    public async Task DemoAccounts_CanSignIn(string email)
    {
        await using (var db = Database.CreateDbContext())
        {
            await new DemoDataSeeder(db, new PasswordHasher<User>(), NullLogger.Instance).SeedAsync();
        }

        await using var auth = Database.CreateDbContext();
        var services = new TestServiceFactory(auth, TestServiceFactory.Context());

        var login = await services.Auth.LoginAsync(new LoginRequest
        {
            Username = email,
            Password = DemoDataSeeder.DemoPassword
        });

        Assert.NotNull(login);
    }
}
