using IronForge.Application.Agents.Delegations;
using IronForge.Application.Agents.DTOs;
using IronForge.Application.Agents.Runtime;
using IronForge.Application.Auth.Models;
using IronForge.Application.Auth.Services;
using IronForge.Application.Entities;
using IronForge.Application.IntegrationTests.Infrastructure;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Tenants;
using IronForge.Shared.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace IronForge.Application.IntegrationTests.Agents;

/// <summary>
/// Agent capability comes from tool bindings only, and model profiles are
/// limited per tenant by plan plus add-ons.
/// </summary>
public sealed class AgentCapabilityAndProfileTests
    : IntegrationTestBase,
      IClassFixture<PostgreSqlTestDatabase>
{
    private const int UserId = 1;

    public AgentCapabilityAndProfileTests(
        PostgreSqlTestDatabase database) : base(database)
    {
    }

    // ---- Capability = tool bindings ----------------------------------------

    [Fact]
    public async Task AgentPermissions_AreThePermissionsOfItsBoundTools()
    {
        var tenantId = await SeedTenantAsync(SubscriptionPlans.Professional.Id);
        await SeedAgentAsync(tenantId, "analyst", "standard", "search_members", "search_audit_events");

        var permissions = await AsAdmin(tenantId, s =>
            s.AgentPermissions.GetPermissionsAsync("analyst"));

        Assert.Equal(
            [Permissions.AuditRead, Permissions.GymMembersRead],
            permissions.Order());
    }

    [Fact]
    public async Task AgentPermissions_ExcludeToolsOfDisabledModules()
    {
        var tenantId = await SeedTenantAsync(SubscriptionPlans.Starter.Id);
        await SeedAgentAsync(tenantId, "analyst", "economy", "search_members", "search_audit_events");

        var permissions = await AsAdmin(tenantId, s =>
            s.AgentPermissions.GetPermissionsAsync("analyst"));

        Assert.Equal([Permissions.GymMembersRead], permissions);
    }

    [Fact]
    public async Task AgentPermissions_OfAnotherTenantsAgent_AreEmpty()
    {
        var acme = await SeedTenantAsync(SubscriptionPlans.Professional.Id);
        var globex = await SeedTenantAsync(SubscriptionPlans.Professional.Id);
        await SeedAgentAsync(globex, "analyst", "standard", "search_members");

        var permissions = await AsAdmin(acme, s =>
            s.AgentPermissions.GetPermissionsAsync("analyst"));

        Assert.Empty(permissions);
    }

    [Fact]
    public async Task AgentDto_ShowsDerivedPermissions()
    {
        var tenantId = await SeedTenantAsync(SubscriptionPlans.Professional.Id);
        await SeedAgentAsync(tenantId, "writer", "standard", "search_members", "freeze_membership");

        var agent = await AsAdmin(tenantId, s => s.AgentManagement.GetAgentAsync("writer"));

        Assert.Equal(
            [Permissions.GymMembersManage, Permissions.GymMembersRead],
            agent.Data!.Permissions);
    }

    [Fact]
    public async Task Delegation_ScopeOutsideTheAgentsTools_IsRejected()
    {
        // The user (an Admin) holds gym.billing.manage, but the agent has no billing tool.
        var tenantId = await SeedTenantAsync(SubscriptionPlans.Professional.Id);
        await SeedAgentAsync(tenantId, "reader", "standard", "search_members");

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            AsAdmin(tenantId, s =>
                new AgentDelegationService(
                    s.ContextAccessor,
                    new UserAuthorizationService(s.ContextAccessor),
                    s.AgentPermissions,
                    store: null!,
                    s.Audit,
                    agentMcpClientAuthorizationService: null!)
                .CreateAsync(
                    "reader",
                    "mcp-client",
                    [Permissions.GymBillingManage],
                    TimeSpan.FromMinutes(30))));

        Assert.Equal(
            "Agent 'reader' is not allowed to use permission 'gym.billing.manage'.",
            exception.Message);
    }

    // ---- Model profiles per tenant -------------------------------------------

    [Theory]
    [InlineData("starter", new string[0], new[] { "economy" })]
    [InlineData("professional", new string[0], new[] { "economy", "standard" })]
    [InlineData("starter", new[] { "premium" }, new[] { "economy", "premium" })]
    public async Task ModelProfileList_FollowsPlanAndAddOns(
        string plan,
        string[] addOns,
        string[] expected)
    {
        var tenantId = await SeedTenantAsync(plan, addOns);

        var profiles = await AsAdmin(tenantId, s => s.AgentManagement.GetModelProfilesAsync());

        Assert.Equal(expected, profiles.Data!.Select(x => x.Name).Order());
    }

    [Fact]
    public async Task CreateAgent_WithProfileOutsideTheSubscription_IsRejected()
    {
        var tenantId = await SeedTenantAsync(SubscriptionPlans.Starter.Id);

        var result = await AsAdmin(tenantId, s => s.AgentManagement.CreateAgentAsync(
            NewAgent("helper", ModelProfileNames.Standard)));

        Assert.Equal(400, result.StatusCode);
        Assert.Contains("not included in this workspace's subscription", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateAgent_WithoutProfile_UsesTheSubscriptionDefault()
    {
        var tenantId = await SeedTenantAsync(SubscriptionPlans.Starter.Id);

        var result = await AsAdmin(tenantId, s => s.AgentManagement.CreateAgentAsync(
            NewAgent("helper", modelProfile: null)));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(ModelProfileNames.Economy, result.Data!.ModelProfile);
    }

    [Fact]
    public async Task Runtime_AgentOnAProfileNoLongerIncluded_FallsBackToTheDefault()
    {
        // E.g. created on Professional, then downgraded to Starter.
        var tenantId = await SeedTenantAsync(SubscriptionPlans.Starter.Id);
        await SeedAgentAsync(tenantId, "legacy", ModelProfileNames.Standard, "search_members");

        var definition = await AsAdmin(tenantId, s =>
            s.AgentDefinitions.GetRequiredAsync("legacy"));

        Assert.Equal(ModelProfileNames.Economy, definition.ModelProfile);
    }

    [Fact]
    public async Task Provision_OnStarter_InstallsTemplateAgentsOnAnIncludedProfile()
    {
        var result = await AsPlatformAdmin(s => s.Provisioning.ProvisionAsync(
            new ProvisionTenantRequest
            {
                Name = "Acme",
                Slug = "acme",
                AdminEmail = "admin@acme.com",
                Plan = SubscriptionPlans.Starter.Id
            }));

        Assert.True(result.Success, result.ErrorMessage);

        await using var db = Database.CreateDbContext();

        var profiles = await db.Agents
            .IgnoreQueryFilters()
            .Where(x => x.TenantId == result.Data!.Tenant.TenantId)
            .Select(x => x.ModelProfile)
            .ToListAsync();

        Assert.All(profiles, x => Assert.Equal(ModelProfileNames.Economy, x));
    }

    [Fact]
    public async Task UpdateSubscription_StoresOnlyProfilesBeyondThePlan_AndRejectsUnknownOnes()
    {
        var tenantId = await SeedTenantAsync(SubscriptionPlans.Starter.Id);

        var updated = await AsPlatformAdmin(s => s.Provisioning.UpdateSubscriptionAsync(
            tenantId,
            new UpdateTenantSubscriptionRequest
            {
                Plan = SubscriptionPlans.Starter.Id,
                SubscriptionStatus = SubscriptionStatus.Active,
                AddOnModelProfiles = ["economy", "premium"]
            }));

        Assert.True(updated.Success, updated.ErrorMessage);
        Assert.Equal(["premium"], updated.Data!.AddOnModelProfiles);
        Assert.Equal(["economy", "premium"], updated.Data.AllowedModelProfiles);

        var unknown = await AsPlatformAdmin(s => s.Provisioning.UpdateSubscriptionAsync(
            tenantId,
            new UpdateTenantSubscriptionRequest
            {
                Plan = SubscriptionPlans.Starter.Id,
                SubscriptionStatus = SubscriptionStatus.Active,
                AddOnModelProfiles = ["gpt-ultra"]
            }));

        Assert.Equal(400, unknown.StatusCode);
    }

    // ---- Helpers ---------------------------------------------------------------

    private static CreateAgentRequest NewAgent(string agentId, string? modelProfile) =>
        new()
        {
            AgentId = agentId,
            Name = agentId,
            Version = "1.0",
            Instructions = "Test agent.",
            ModelProfile = modelProfile
        };

    private async Task<T> AsAdmin<T>(int tenantId, Func<TestServiceFactory, Task<T>> action)
    {
        var context = TestServiceFactory.Context(
            tenantId,
            UserId,
            RolePermissions.GetPermissions(TenantRoles.Admin));

        await using var db = Database.CreateDbContext(context);

        return await action(new TestServiceFactory(db, context));
    }

    private async Task<T> AsPlatformAdmin<T>(Func<TestServiceFactory, Task<T>> action)
    {
        var context = TestServiceFactory.Context(
            tenantId: null,
            userId: null,
            Permissions.PlatformTenantsManage);

        await using var db = Database.CreateDbContext(context);

        return await action(new TestServiceFactory(db, context));
    }

    private async Task<int> SeedTenantAsync(string plan, params string[] modelProfileAddOns)
    {
        await using var db = Database.CreateDbContext();

        var tenant = new Tenant
        {
            Name = "Tenant",
            Slug = $"tenant-{Guid.NewGuid():N}",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            Plan = plan,
            AddOnModelProfiles = modelProfileAddOns.ToList()
        };

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        return tenant.Id;
    }

    private async Task SeedAgentAsync(
        int tenantId,
        string agentId,
        string modelProfile,
        params string[] tools)
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
            ModelProfile = modelProfile,
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
}
