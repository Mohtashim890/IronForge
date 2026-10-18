using IronForge.Application.Agents.Models;
using IronForge.Application.Agents.Runtime;
using IronForge.Application.Auth.DTOs;
using IronForge.Application.Auth.Models;
using IronForge.Application.Entities;
using IronForge.Application.IntegrationTests.Infrastructure;
using IronForge.Infrastructure.Data;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Agents.Manage;
using IronForge.Shared.Models.Tenants;
using IronForge.Shared.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace IronForge.Application.IntegrationTests.Tenancy;

/// <summary>
/// Plans, add-on modules and subscription status: what a tenant may use and
/// whether it may be used at all.
/// </summary>
public sealed class TenantSubscriptionTests
    : IntegrationTestBase,
      IClassFixture<PostgreSqlTestDatabase>
{
    private const string Password = "Correct-Horse-1";
    private const string AuditTool = "search_audit_events";

    public TenantSubscriptionTests(
        PostgreSqlTestDatabase database) : base(database)
    {
    }

    // ---- Provisioning ----------------------------------------------------

    [Fact]
    public async Task Provision_OnStarter_InstallsOnlyAgentsThePlanCovers()
    {
        var result = await AsPlatformAdmin(s => s.Provisioning.ProvisionAsync(
            NewTenant("acme", SubscriptionPlans.Starter.Id, trialDays: 14)));

        Assert.True(result.Success, result.ErrorMessage);

        var tenant = result.Data!.Tenant;

        Assert.Equal(SubscriptionPlans.Starter.Id, tenant.Plan);
        Assert.Equal(SubscriptionStatus.Trial, tenant.SubscriptionStatus);
        Assert.NotNull(tenant.TrialEndsAtUtc);
        Assert.InRange(
            tenant.TrialEndsAtUtc!.Value,
            DateTime.UtcNow.AddDays(13),
            DateTime.UtcNow.AddDays(15));
        Assert.Equal(
            new[] { PlatformModules.GymBilling, PlatformModules.GymCore, PlatformModules.GymEngagement },
            tenant.EnabledModules);

        // The coach needs coaching and Insights needs audit; Starter has neither.
        await using var db = Database.CreateDbContext();

        var agentIds = await db.Agents
            .IgnoreQueryFilters()
            .Where(x => x.TenantId == tenant.TenantId)
            .Select(x => x.AgentId)
            .ToListAsync();

        Assert.Equal([AgentTemplates.GymAdmin.Definition.AgentId], agentIds);

        // Every gym starts with its settings and a join code.
        var settings = await db.GymSettings.SingleAsync(x => x.TenantId == tenant.TenantId);
        Assert.Equal(8, settings.JoinCode.Length);
        Assert.Equal("PKR", settings.Currency);
    }

    [Fact]
    public async Task Provision_WithUnknownPlan_IsRejected()
    {
        var result = await AsPlatformAdmin(s => s.Provisioning.ProvisionAsync(
            NewTenant("acme", "gold")));

        Assert.Equal(400, result.StatusCode);
    }

    // ---- Changing the subscription ---------------------------------------

    [Fact]
    public async Task UpdateSubscription_AddOnModuleIsEnabled_AndPlanModulesAreNotStoredAsAddOns()
    {
        var tenantId = await SeedTenantAsync(SubscriptionPlans.Starter.Id);

        var result = await AsPlatformAdmin(s => s.Provisioning.UpdateSubscriptionAsync(
            tenantId,
            new UpdateTenantSubscriptionRequest
            {
                Plan = SubscriptionPlans.Starter.Id,
                SubscriptionStatus = SubscriptionStatus.Active,
                // gym.core is already in Starter; audit is the add-on.
                AddOnModules = [PlatformModules.Audit, PlatformModules.GymCore]
            }));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal([PlatformModules.Audit], result.Data!.AddOnModules);
        Assert.Equal(
            new[] { PlatformModules.Audit, PlatformModules.GymBilling, PlatformModules.GymCore, PlatformModules.GymEngagement },
            result.Data.EnabledModules);
    }

    [Fact]
    public async Task UpdateSubscription_WithUnknownModule_IsRejected()
    {
        var tenantId = await SeedTenantAsync(SubscriptionPlans.Starter.Id);

        var result = await AsPlatformAdmin(s => s.Provisioning.UpdateSubscriptionAsync(
            tenantId,
            new UpdateTenantSubscriptionRequest
            {
                Plan = SubscriptionPlans.Starter.Id,
                SubscriptionStatus = SubscriptionStatus.Active,
                AddOnModules = ["client.unknown"]
            }));

        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task UpdateSubscription_RequiresPlatformAdmin()
    {
        var tenantId = await SeedTenantAsync(SubscriptionPlans.Starter.Id);

        var result = await AsUser(tenantId, userId: 1, AdminPermissions(), s =>
            s.Provisioning.UpdateSubscriptionAsync(
                tenantId,
                new UpdateTenantSubscriptionRequest
                {
                    Plan = SubscriptionPlans.Enterprise.Id,
                    SubscriptionStatus = SubscriptionStatus.Active
                }));

        Assert.Equal(403, result.StatusCode);
    }

    // ---- Catalogue, tool editor and runtime ------------------------------

    [Fact]
    public async Task ToolCatalogue_OnlyListsToolsOfEnabledModules()
    {
        var starter = await SeedTenantAsync(SubscriptionPlans.Starter.Id);
        var professional = await SeedTenantAsync(SubscriptionPlans.Professional.Id);

        var starterCatalogue = await AsTenantAdmin(starter, s => s.AgentManagement.GetToolCatalogAsync());
        var professionalCatalogue = await AsTenantAdmin(professional, s => s.AgentManagement.GetToolCatalogAsync());

        Assert.DoesNotContain(starterCatalogue.Data!, x => x.Name == AuditTool);
        Assert.All(starterCatalogue.Data!, x => Assert.Contains(x.Module, SubscriptionPlans.Starter.Modules));
        Assert.DoesNotContain(starterCatalogue.Data!, x => x.Name == "log_workout");

        Assert.Contains(professionalCatalogue.Data!, x => x.Name == AuditTool);
    }

    [Fact]
    public async Task ToolEditor_RejectsToolOutsideTheSubscription()
    {
        var tenantId = await SeedTenantAsync(SubscriptionPlans.Starter.Id);
        await SeedAgentAsync(tenantId, "analyst", "search_members");

        var result = await AsTenantAdmin(tenantId, s => s.AgentManagement.UpdateToolsAsync(
            "analyst",
            new UpdateAgentToolsRequest
            {
                Tools =
                [
                    new AgentToolBindingDto { ToolName = "search_members" },
                    new AgentToolBindingDto { ToolName = AuditTool }
                ]
            }));

        Assert.Equal(400, result.StatusCode);
        Assert.Contains("not included in this workspace's subscription", result.ErrorMessage);
    }

    [Fact]
    public async Task AgentDefinition_DropsToolsOfDisabledModules_UntilTheModuleIsEnabled()
    {
        var tenantId = await SeedTenantAsync(SubscriptionPlans.Starter.Id);

        // A binding made earlier, e.g. before a downgrade.
        await SeedAgentAsync(tenantId, "analyst", "search_members", AuditTool);

        var onStarter = await AsTenantAdmin(tenantId, s =>
            s.AgentDefinitions.GetRequiredAsync("analyst"));

        Assert.Equal(["search_members"], onStarter.Tools.Select(x => x.ToolName));

        await AsPlatformAdmin(s => s.Provisioning.UpdateSubscriptionAsync(
            tenantId,
            new UpdateTenantSubscriptionRequest
            {
                Plan = SubscriptionPlans.Starter.Id,
                SubscriptionStatus = SubscriptionStatus.Active,
                AddOnModules = [PlatformModules.Audit]
            }));

        var withAddOn = await AsTenantAdmin(tenantId, s =>
            s.AgentDefinitions.GetRequiredAsync("analyst"));

        Assert.Equal(["search_members", AuditTool], withAddOn.Tools.Select(x => x.ToolName));
    }

    // ---- Agents follow the subscription ------------------------------------

    [Fact]
    public async Task CoachingAddOn_InstallsTheCoach_ForMembers_AndDowngradeHidesIt()
    {
        var provisioned = await AsPlatformAdmin(s => s.Provisioning.ProvisionAsync(
            NewTenant("hardstone", SubscriptionPlans.Starter.Id)));
        var tenantId = provisioned.Data!.Tenant.TenantId;

        Assert.DoesNotContain(await AvailableAgentsAsync(tenantId, TenantRoles.Member), x => x == AgentIds.FitnessCoach);

        // The operator adds coaching to the Starter plan.
        var addOn = await AsPlatformAdmin(s => s.Provisioning.UpdateSubscriptionAsync(
            tenantId,
            new UpdateTenantSubscriptionRequest
            {
                Plan = SubscriptionPlans.Starter.Id,
                SubscriptionStatus = SubscriptionStatus.Active,
                AddOnModules = [PlatformModules.GymCoaching]
            }));

        Assert.True(addOn.Success, addOn.ErrorMessage);
        Assert.Equal([AgentIds.FitnessCoach], await AvailableAgentsAsync(tenantId, TenantRoles.Member));

        var coach = await AsRole(tenantId, TenantRoles.Member, s =>
            s.AgentDefinitions.GetRequiredAsync(AgentIds.FitnessCoach));
        Assert.Contains(coach.Tools, x => x.ToolName == "log_workout");

        // Back to plain Starter: the coach stays installed but is neither offered nor runnable.
        await AsPlatformAdmin(s => s.Provisioning.UpdateSubscriptionAsync(
            tenantId,
            new UpdateTenantSubscriptionRequest
            {
                Plan = SubscriptionPlans.Starter.Id,
                SubscriptionStatus = SubscriptionStatus.Active
            }));

        Assert.Empty(await AvailableAgentsAsync(tenantId, TenantRoles.Member));
        await Assert.ThrowsAsync<InvalidOperationException>(() => AsRole(tenantId, TenantRoles.Member, s =>
            s.AgentDefinitions.GetRequiredAsync(AgentIds.FitnessCoach)));
    }

    [Fact]
    public async Task PlanUpgrade_InstallsTheAgentsTheNewPlanCovers()
    {
        var provisioned = await AsPlatformAdmin(s => s.Provisioning.ProvisionAsync(
            NewTenant("upgrader", SubscriptionPlans.Starter.Id)));
        var tenantId = provisioned.Data!.Tenant.TenantId;

        var upgrade = await AsPlatformAdmin(s => s.Provisioning.UpdateSubscriptionAsync(
            tenantId,
            new UpdateTenantSubscriptionRequest
            {
                Plan = SubscriptionPlans.Professional.Id,
                SubscriptionStatus = SubscriptionStatus.Active
            }));

        Assert.True(upgrade.Success, upgrade.ErrorMessage);
        Assert.Equal([AgentIds.FitnessCoach], await AvailableAgentsAsync(tenantId, TenantRoles.Member));
        Assert.Equal(
            new[] { AgentIds.GymAdmin, AgentIds.InsightsAgent }.Order(),
            (await AvailableAgentsAsync(tenantId, TenantRoles.Admin)).Order());

        await using var db = Database.CreateDbContext();
        var installed = await db.Agents.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId)
            .Select(x => x.AgentId)
            .ToListAsync();

        Assert.Equal(AgentTemplates.Default.Count, installed.Count);
    }

    [Fact]
    public async Task GymAdminAgent_OffersTheExerciseLibrary_OnlyWithCoaching()
    {
        var starter = (await AsPlatformAdmin(s => s.Provisioning.ProvisionAsync(
            NewTenant("plain", SubscriptionPlans.Starter.Id)))).Data!.Tenant.TenantId;
        var professional = (await AsPlatformAdmin(s => s.Provisioning.ProvisionAsync(
            NewTenant("coached", SubscriptionPlans.Professional.Id)))).Data!.Tenant.TenantId;

        var onStarter = await AsRole(starter, TenantRoles.Trainer, s =>
            s.AgentDefinitions.GetRequiredAsync(AgentIds.GymAdmin));
        Assert.DoesNotContain(onStarter.Tools, x => x.ToolName == "list_exercises");

        var onProfessional = await AsRole(professional, TenantRoles.Trainer, s =>
            s.AgentDefinitions.GetRequiredAsync(AgentIds.GymAdmin));
        Assert.Contains(onProfessional.Tools, x => x.ToolName == "list_exercises");
        Assert.Contains(onProfessional.Tools, x => x.ToolName == "get_member_routines");

        // A Trainer may use them (user ∩ agent), and the library answers.
        var exercises = await AsRole(professional, TenantRoles.Trainer, s =>
            s.Routines.ListExercisesAsync(null, null));
        Assert.True(exercises.Success, exercises.ErrorMessage);

        var blocked = await AsRole(starter, TenantRoles.Trainer, s =>
            s.Routines.ListExercisesAsync(null, null));
        Assert.Equal(403, blocked.StatusCode);
    }

    private async Task<List<string>> AvailableAgentsAsync(int tenantId, string role)
    {
        var result = await AsRole(tenantId, role, s => s.AgentManagement.GetAvailableAgentsAsync());

        Assert.True(result.Success, result.ErrorMessage);

        return result.Data!.Select(x => x.AgentId).ToList();
    }

    private async Task<T> AsRole<T>(int tenantId, string role, Func<TestServiceFactory, Task<T>> action)
    {
        var context = TestServiceFactory.AsRole(tenantId, userId: 1, role);

        await using var db = Database.CreateDbContext(context);

        return await action(new TestServiceFactory(db, context));
    }

    // ---- Quota -------------------------------------------------------------

    [Fact]
    public async Task Quota_UsesPlanLimit_UnlessTheTenantOverridesIt()
    {
        var tenantId = await SeedTenantAsync(SubscriptionPlans.Starter.Id);

        var fromPlan = await AsTenantAdmin(tenantId, s => s.Quota.GetSummaryAsync());
        Assert.Equal(SubscriptionPlans.Starter.MonthlyAgentTokenLimit, fromPlan.MonthlyTokenLimit);

        await using (var db = Database.CreateDbContext())
        {
            var tenant = await db.Tenants.SingleAsync(x => x.Id == tenantId);
            tenant.MonthlyAgentTokenLimit = 123_456;
            await db.SaveChangesAsync();
        }

        var overridden = await AsTenantAdmin(tenantId, s => s.Quota.GetSummaryAsync());
        Assert.Equal(123_456, overridden.MonthlyTokenLimit);
    }

    // ---- Access --------------------------------------------------------------

    [Theory]
    [InlineData(SubscriptionStatus.Active, null, true)]
    [InlineData(SubscriptionStatus.PastDue, null, true)]
    [InlineData(SubscriptionStatus.Trial, null, true)]
    [InlineData(SubscriptionStatus.Trial, 1, true)]
    [InlineData(SubscriptionStatus.Trial, -1, false)]
    [InlineData(SubscriptionStatus.Canceled, null, false)]
    public async Task SelectTenant_FollowsSubscriptionStatus(
        SubscriptionStatus status,
        int? trialEndsInDays,
        bool expectedAccess)
    {
        var tenantId = await SeedTenantAsync(
            SubscriptionPlans.Starter.Id,
            status,
            trialEndsInDays is { } days ? DateTime.UtcNow.AddDays(days) : null);

        var userId = await SeedMemberAsync(tenantId, "member");

        var login = await LoginAsync("member");

        await using var db = Database.CreateDbContext();

        var selected = await new TestServiceFactory(db, TestServiceFactory.Context())
            .Auth
            .SelectTenantAsync(userId, tenantId, login.RefreshToken);

        Assert.Equal(expectedAccess, selected != null);
    }

    [Fact]
    public async Task Refresh_AfterSubscriptionCanceled_EndsTheSession()
    {
        var tenantId = await SeedTenantAsync(SubscriptionPlans.Starter.Id);
        var userId = await SeedMemberAsync(tenantId, "member");

        var login = await LoginAsync("member");

        LoginResponse selected;

        await using (var db = Database.CreateDbContext())
        {
            selected = (await new TestServiceFactory(db, TestServiceFactory.Context())
                .Auth
                .SelectTenantAsync(userId, tenantId, login.RefreshToken))!;
        }

        await AsPlatformAdmin(s => s.Provisioning.UpdateSubscriptionAsync(
            tenantId,
            new UpdateTenantSubscriptionRequest
            {
                Plan = SubscriptionPlans.Starter.Id,
                SubscriptionStatus = SubscriptionStatus.Canceled
            }));

        await using (var db = Database.CreateDbContext())
        {
            var refreshed = await new TestServiceFactory(db, TestServiceFactory.Context())
                .Auth
                .RefreshTokenAsync(selected.RefreshToken);

            Assert.Null(refreshed);
        }
    }

    [Fact]
    public async Task CanceledTenant_IsNotListedForItsMembers()
    {
        var active = await SeedTenantAsync(SubscriptionPlans.Starter.Id);
        var canceled = await SeedTenantAsync(SubscriptionPlans.Starter.Id, SubscriptionStatus.Canceled);

        var userId = await SeedMemberAsync(active, "member");
        await AddMembershipAsync(canceled, userId);

        var tenants = await AsUser(null, userId, [], s =>
            new IronForge.Application.Tenants.Services.TenantService(
                new IronForge.Infrastructure.Persistence.EfRepository<TenantMembership>(s.Db),
                new IronForge.Infrastructure.Persistence.EfRepository<Tenant>(s.Db),
                s.ContextAccessor,
                s.Audit).GetMyTenantsAsync());

        Assert.Equal([active], tenants.Select(x => x.TenantId));
    }

    // ---- Helpers -----------------------------------------------------------

    private static ProvisionTenantRequest NewTenant(
        string slug,
        string plan,
        int? trialDays = null) =>
        new()
        {
            Name = slug,
            Slug = slug,
            AdminEmail = $"admin@{slug}.com",
            Plan = plan,
            TrialDays = trialDays,
            Currency = "PKR",
            TimeZoneId = "Asia/Karachi"
        };

    private static string[] AdminPermissions() =>
        RolePermissions.GetPermissions(TenantRoles.Admin);

    private async Task<T> AsUser<T>(
        int? tenantId,
        int? userId,
        string[] permissions,
        Func<TestServiceFactory, Task<T>> action)
    {
        var context = TestServiceFactory.Context(tenantId, userId, permissions);

        await using var db = Database.CreateDbContext(context);

        return await action(new TestServiceFactory(db, context));
    }

    private Task<T> AsPlatformAdmin<T>(Func<TestServiceFactory, Task<T>> action) =>
        AsUser(null, null, [Permissions.PlatformTenantsManage], action);

    private Task<T> AsTenantAdmin<T>(int tenantId, Func<TestServiceFactory, Task<T>> action) =>
        AsUser(tenantId, userId: 1, AdminPermissions(), action);

    private int _tenantCounter;

    private async Task<int> SeedTenantAsync(
        string plan,
        SubscriptionStatus status = SubscriptionStatus.Active,
        DateTime? trialEndsAtUtc = null)
    {
        await using var db = Database.CreateDbContext();

        var slug = $"tenant-{++_tenantCounter}";

        var tenant = new Tenant
        {
            Name = slug,
            Slug = slug,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            Plan = plan,
            SubscriptionStatus = status,
            TrialEndsAtUtc = trialEndsAtUtc
        };

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        return tenant.Id;
    }

    private async Task SeedAgentAsync(int tenantId, string agentId, params string[] tools)
    {
        await using var db = Database.CreateDbContext();

        var agent = new Agent
        {
            TenantId = tenantId,
            AgentId = agentId,
            Name = agentId,
            Version = "1.0",
            IsActive = true,
            Instructions = "Test agent.",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        foreach (var tool in tools)
        {
            agent.ToolBindings.Add(new AgentToolBindingEntity { ToolName = tool });
        }

        db.Agents.Add(agent);
        await db.SaveChangesAsync();
    }

    private async Task<int> SeedMemberAsync(int tenantId, string username)
    {
        int userId;

        await using (var db = Database.CreateDbContext())
        {
            var user = new User
            {
                Username = username,
                Email = $"{username}@example.com",
                Role = PlatformRoles.User,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            };

            user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Password);

            db.Users.Add(user);
            await db.SaveChangesAsync();

            userId = user.Id;
        }

        await AddMembershipAsync(tenantId, userId);

        return userId;
    }

    private async Task AddMembershipAsync(int tenantId, int userId)
    {
        await using var db = Database.CreateDbContext();

        db.TenantMemberships.Add(new TenantMembership
        {
            TenantId = tenantId,
            UserId = userId,
            Role = TenantRoles.Member,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }

    private async Task<LoginResponse> LoginAsync(string username)
    {
        await using var db = Database.CreateDbContext();

        var login = await new TestServiceFactory(db, TestServiceFactory.Context())
            .Auth
            .LoginAsync(new LoginRequest { Username = username, Password = Password });

        Assert.NotNull(login);

        return login!;
    }
}
