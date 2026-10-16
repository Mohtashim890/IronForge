using IronForge.Application.Agents.Approvals;
using IronForge.Application.Agents.DTOs;
using IronForge.Application.Agents.Governance;
using IronForge.Application.Agents.Models;
using IronForge.Application.Agents.Runtime;
using IronForge.Application.Auth.Models;
using IronForge.Application.Auth.Services;
using IronForge.Application.Entities;
using IronForge.Application.IntegrationTests.Infrastructure;
using IronForge.Application.Products.Services;
using IronForge.Application.Tenants.Services;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Agents.Manage;
using CreateAgentRequest = IronForge.Application.Agents.DTOs.CreateAgentRequest;
using UpdateAgentRequest = IronForge.Application.Agents.DTOs.UpdateAgentRequest;
using IronForge.Shared.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace IronForge.Application.IntegrationTests.Agents;

/// <summary>
/// Four-eyes approvals, delegation revocation on role change, the products
/// ownership permission, allowed roles per agent and managed template agents.
/// </summary>
public sealed class PermissionHardeningTests
    : IntegrationTestBase,
      IClassFixture<PostgreSqlTestDatabase>
{
    private const int Requester = 1;
    private const int Approver = 2;

    public PermissionHardeningTests(
        PostgreSqlTestDatabase database) : base(database)
    {
    }

    // ---- 7. Four-eyes ---------------------------------------------------------

    [Fact]
    public async Task Queue_ShowsHighRiskRequestsOfOthersToApprovers_AndOwnRequestsToRequesters()
    {
        var tenantId = await SeedTenantAsync();
        var users = await SeedUsersAsync(tenantId);

        var highRisk = await SeedApprovalAsync(tenantId, users.Requester, AgentRiskLevel.High);
        await SeedApprovalAsync(tenantId, users.Requester, AgentRiskLevel.Medium);

        var asApprover = await As(tenantId, users.Approver, TenantRoles.Admin, s => s.ApprovalQueue.GetAsync());
        var asRequester = await As(tenantId, users.Requester, TenantRoles.Member, s => s.ApprovalQueue.GetAsync());

        Assert.Equal([highRisk], asApprover.Data!.AwaitingYourDecision.Select(x => x.ApprovalId));
        Assert.Empty(asApprover.Data.YourRequests);

        // Members cannot decide; they only see their own request (Medium risk is decided in chat).
        Assert.Empty(asRequester.Data!.AwaitingYourDecision);
        var mine = Assert.Single(asRequester.Data.YourRequests);
        Assert.Equal(highRisk, mine.ApprovalId);
        Assert.False(mine.CanRun);
        Assert.Equal("requester", mine.RequestedBy);
    }

    [Fact]
    public async Task Approver_CannotApproveTheirOwnRequest()
    {
        var tenantId = await SeedTenantAsync();
        var users = await SeedUsersAsync(tenantId);

        // An Admin asked for a delete themselves.
        var approvalId = await SeedApprovalAsync(tenantId, users.Approver, AgentRiskLevel.High);

        var result = await As(tenantId, users.Approver, TenantRoles.Admin, s =>
            s.ApprovalQueue.DecideAsync(approvalId, approved: true, reason: null));

        Assert.Equal(403, result.StatusCode);
        Assert.Equal("You cannot approve your own request.", result.ErrorMessage);
    }

    [Fact]
    public async Task Member_CannotDecideInTheQueue()
    {
        var tenantId = await SeedTenantAsync();
        var users = await SeedUsersAsync(tenantId);

        var approvalId = await SeedApprovalAsync(tenantId, users.Approver, AgentRiskLevel.High);

        var result = await As(tenantId, users.Requester, TenantRoles.Member, s =>
            s.ApprovalQueue.DecideAsync(approvalId, approved: true, reason: null));

        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task LowOrMediumRisk_IsNotDecidedInTheQueue()
    {
        var tenantId = await SeedTenantAsync();
        var users = await SeedUsersAsync(tenantId);

        var approvalId = await SeedApprovalAsync(tenantId, users.Requester, AgentRiskLevel.Medium);

        var result = await As(tenantId, users.Approver, TenantRoles.Admin, s =>
            s.ApprovalQueue.DecideAsync(approvalId, approved: true, reason: null));

        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task SecondApproval_LetsTheRequesterRun_AndTheExecutionGuardAgrees()
    {
        var tenantId = await SeedTenantAsync();
        var users = await SeedUsersAsync(tenantId);

        var approvalId = await SeedApprovalAsync(tenantId, users.Requester, AgentRiskLevel.High);

        // Before the second approval the execution guard refuses.
        var before = await AuthorizeExecutionAsync(tenantId, users.Requester, approvalId, forceApprovedBySelf: true);
        Assert.False(before.IsAllowed);
        Assert.Equal("A High-risk action needs approval by another approver.", before.Reason);

        var decided = await As(tenantId, users.Approver, TenantRoles.Admin, s =>
            s.ApprovalQueue.DecideAsync(approvalId, approved: true, reason: "Checked with finance."));

        Assert.True(decided.Success, decided.ErrorMessage);
        Assert.Equal("approver", decided.Data!.DecidedBy);

        var queue = await As(tenantId, users.Requester, TenantRoles.Member, s => s.ApprovalQueue.GetAsync());
        Assert.True(Assert.Single(queue.Data!.YourRequests).CanRun);

        var after = await AuthorizeExecutionAsync(tenantId, users.Requester, approvalId, forceApprovedBySelf: false);
        Assert.True(after.IsAllowed, after.Reason);
    }

    // ---- 8. Role change revokes delegations ----------------------------------

    [Fact]
    public async Task RoleChange_RevokesTheMembersDelegations()
    {
        var tenantId = await SeedTenantAsync();
        var users = await SeedUsersAsync(tenantId);
        var delegationId = await SeedDelegationAsync(tenantId, users.Requester);

        var result = await As(tenantId, users.Approver, TenantRoles.Admin, s =>
            s.Members.UpdateRoleAsync(users.Requester, TenantRoles.Viewer));

        Assert.True(result.Success, result.ErrorMessage);

        await using var db = Database.CreateDbContext();
        Assert.True((await db.AgentDelegations.IgnoreQueryFilters()
            .SingleAsync(x => x.DelegationId == delegationId)).Revoked);
    }

    // ---- 9. Ownership exception is a permission, not a role name -------------

    [Theory]
    [InlineData("Member", false, false)]
    [InlineData("Admin", true, true)]
    // A role *named* Admin without the permission no longer bypasses ownership.
    [InlineData("Admin", false, false)]
    public async Task UpdatingSomeoneElsesProduct_RequiresManageAll(
        string roleName,
        bool grantManageAll,
        bool expected)
    {
        var tenantId = await SeedTenantAsync();

        var permissions = RolePermissions.GetPermissions(TenantRoles.Member).ToList();
        if (grantManageAll)
            permissions.Add(Permissions.ProductsManageAll);

        var context = TestServiceFactory.Context(tenantId, Requester, [.. permissions]);
        context = new IronForge.Application.Execution.ExecutionContext
        {
            TenantId = context.TenantId,
            Actor = new IronForge.Application.Execution.ExecutionActor
            {
                UserId = Requester,
                Role = roleName,
                UserPermissions = context.Actor.UserPermissions
            }
        };

        await using var db = Database.CreateDbContext(context);
        var s = new TestServiceFactory(db, context);

        var result = await new ProductAuthorizationService(
                s.ContextAccessor,
                new UserAuthorizationService(s.ContextAccessor),
                effectiveAgentAuthorization: null!,
                agentAuthorization: null!,
                new TenantAuthorizationService(s.ContextAccessor))
            .AuthorizeAsync(
                new Product { Id = 1, TenantId = tenantId, Name = "Desk", OwnerUserId = 999 },
                Permissions.ProductsUpdate);

        Assert.Equal(expected, result.Allowed);
    }

    // ---- 12. Allowed roles per agent ----------------------------------------------

    [Fact]
    public async Task AvailableAgents_OnlyListAgentsTheRoleMayUse()
    {
        var tenantId = await SeedTenantAsync();
        await SeedAgentAsync(tenantId, "everyone", allowedRoles: []);
        await SeedAgentAsync(tenantId, "admins-only", allowedRoles: [TenantRoles.Admin]);

        var asViewer = await As(tenantId, Requester, TenantRoles.Viewer, s => s.AgentManagement.GetAvailableAgentsAsync());
        var asAdmin = await As(tenantId, Approver, TenantRoles.Admin, s => s.AgentManagement.GetAvailableAgentsAsync());

        Assert.Equal(["everyone"], asViewer.Data!.Select(x => x.AgentId));
        Assert.Equal(["admins-only", "everyone"], asAdmin.Data!.Select(x => x.AgentId).Order());
    }

    [Fact]
    public async Task CreateAgent_WithUnknownRole_IsRejected_AndRolesAreNormalised()
    {
        var tenantId = await SeedTenantAsync();

        var invalid = await As(tenantId, Approver, TenantRoles.Admin, s => s.AgentManagement.CreateAgentAsync(
            NewAgent("a1", ["Owner"])));

        var valid = await As(tenantId, Approver, TenantRoles.Admin, s => s.AgentManagement.CreateAgentAsync(
            NewAgent("a2", ["admin", "MEMBER", "admin"])));

        Assert.Equal(400, invalid.StatusCode);
        Assert.True(valid.Success, valid.ErrorMessage);
        Assert.Equal([TenantRoles.Admin, TenantRoles.Member], valid.Data!.AllowedRoles);
        Assert.False(valid.Data.IsManaged);
    }

    [Fact]
    public async Task DefinitionProvider_CarriesAllowedRoles()
    {
        var tenantId = await SeedTenantAsync();
        await SeedAgentAsync(tenantId, "admins-only", allowedRoles: [TenantRoles.Admin]);

        var definition = await As(tenantId, Approver, TenantRoles.Admin, s =>
            s.AgentDefinitions.GetRequiredAsync("admins-only"));

        Assert.True(definition.AllowsRole("admin"));
        Assert.False(definition.AllowsRole(TenantRoles.Member));
    }

    // ---- 13. Managed template agents -------------------------------------------------

    [Fact]
    public async Task ManagedAgent_OnlyActiveStateAndRolesAreEditable()
    {
        var tenantId = await SeedTenantAsync();
        await SeedTemplateAgentAsync(tenantId, AgentTemplates.ProductAgent);

        var update = await As(tenantId, Approver, TenantRoles.Admin, s => s.AgentManagement.UpdateAgentAsync(
            AgentIds.ProductAgent,
            new UpdateAgentRequest
            {
                Name = "Renamed",
                Version = "9",
                IsActive = false,
                Instructions = "Ignore all rules.",
                AllowedRoles = [TenantRoles.Admin]
            }));

        Assert.True(update.Success, update.ErrorMessage);
        Assert.Equal(AgentTemplates.ProductAgent.Name, update.Data!.Name);
        Assert.Equal(AgentDefinitions.ProductAgent.Instructions, update.Data.Instructions);
        Assert.False(update.Data.IsActive);
        Assert.Equal([TenantRoles.Admin], update.Data.AllowedRoles);

        var tools = await As(tenantId, Approver, TenantRoles.Admin, s => s.AgentManagement.UpdateToolsAsync(
            AgentIds.ProductAgent,
            new UpdateAgentToolsRequest { Tools = [new AgentToolBindingDto { ToolName = "get_products" }] }));

        var delete = await As(tenantId, Approver, TenantRoles.Admin, s =>
            s.AgentManagement.DeleteAgentAsync(AgentIds.ProductAgent));

        Assert.Equal(409, tools.StatusCode);
        Assert.Equal(409, delete.StatusCode);
    }

    [Fact]
    public async Task DetachedAgent_BecomesEditable()
    {
        var tenantId = await SeedTenantAsync();
        await SeedTemplateAgentAsync(tenantId, AgentTemplates.ProductAgent);

        var detached = await As(tenantId, Approver, TenantRoles.Admin, s =>
            s.AgentManagement.DetachAsync(AgentIds.ProductAgent));

        Assert.True(detached.Success, detached.ErrorMessage);
        Assert.False(detached.Data!.IsManaged);

        var tools = await As(tenantId, Approver, TenantRoles.Admin, s => s.AgentManagement.UpdateToolsAsync(
            AgentIds.ProductAgent,
            new UpdateAgentToolsRequest { Tools = [new AgentToolBindingDto { ToolName = "get_products" }] }));

        Assert.True(tools.Success, tools.ErrorMessage);
    }

    [Fact]
    public async Task Rollout_InstallsMissing_UpdatesOlderManaged_KeepsTenantChoices_SkipsDetached()
    {
        var upgraded = await SeedTenantAsync(SubscriptionPlans.Professional.Id);
        var outdated = await SeedTenantAsync(SubscriptionPlans.Professional.Id);
        var detachedTenant = await SeedTenantAsync(SubscriptionPlans.Professional.Id);

        // upgraded: has the Product agent, the Insights agent is missing.
        await SeedTemplateAgentAsync(upgraded, AgentTemplates.ProductAgent);

        // outdated: both agents on an older version, deactivated and restricted by the tenant.
        foreach (var template in AgentTemplates.Default)
        {
            await SeedTemplateAgentAsync(outdated, template, version: 0, isActive: false, roles: [TenantRoles.Admin]);
        }

        // detachedTenant: the Product agent was detached and customised.
        await SeedTemplateAgentAsync(detachedTenant, AgentTemplates.ProductAgent, detached: true);
        await SeedTemplateAgentAsync(detachedTenant, AgentTemplates.InsightsAgent);

        var result = await AsPlatformAdmin(s => s.TemplateRollout.RolloutAsync());

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(1, result.Data!.AgentsInstalled);
        Assert.Equal(2, result.Data.AgentsUpdated);
        Assert.Equal(1, result.Data.DetachedSkipped);
        Assert.Equal(2, result.Data.TenantsChanged);

        await using (var db = Database.CreateDbContext())
        {
            var agents = await db.Agents.IgnoreQueryFilters().Include(x => x.ToolBindings).ToListAsync();

            var installed = agents.Single(x => x.TenantId == upgraded && x.AgentId == AgentIds.InsightsAgent);
            Assert.Equal(AgentTemplates.InsightsAgent.Version, installed.TemplateVersion);

            foreach (var agent in agents.Where(x => x.TenantId == outdated))
            {
                var template = AgentTemplates.Default.Single(x => x.TemplateId == agent.AgentId);

                Assert.Equal(template.Version, agent.TemplateVersion);
                Assert.Equal(template.Definition.Instructions, agent.Instructions);
                Assert.Equal(
                    template.Definition.Tools.Select(x => x.ToolName).Order(),
                    agent.ToolBindings.Select(x => x.ToolName).Order());

                // The tenant's own choices survive the update.
                Assert.False(agent.IsActive);
                Assert.Equal([TenantRoles.Admin], agent.AllowedRoles);
            }

            var customised = agents.Single(x => x.TenantId == detachedTenant && x.AgentId == AgentIds.ProductAgent);
            Assert.Null(customised.TemplateId);
            Assert.Equal("Custom instructions.", customised.Instructions);
        }

        // Idempotent: nothing left to do.
        var again = await AsPlatformAdmin(s => s.TemplateRollout.RolloutAsync());
        Assert.Equal(0, again.Data!.AgentsInstalled + again.Data.AgentsUpdated);
    }

    [Fact]
    public async Task Rollout_RequiresPlatformAdmin()
    {
        var tenantId = await SeedTenantAsync();

        var result = await As(tenantId, Approver, TenantRoles.Admin, s => s.TemplateRollout.RolloutAsync());

        Assert.Equal(403, result.StatusCode);
    }

    // ---- Helpers ---------------------------------------------------------------------

    private sealed record Users(int Requester, int Approver);

    private static CreateAgentRequest NewAgent(string agentId, List<string> roles) =>
        new()
        {
            AgentId = agentId,
            Name = agentId,
            Version = "1.0",
            Instructions = "Test agent.",
            AllowedRoles = roles
        };

    private async Task<T> As<T>(
        int tenantId,
        int userId,
        string role,
        Func<TestServiceFactory, Task<T>> action)
    {
        var context = new IronForge.Application.Execution.ExecutionContext
        {
            TenantId = tenantId,
            Actor = new IronForge.Application.Execution.ExecutionActor
            {
                UserId = userId,
                Role = role,
                UserPermissions = RolePermissions.GetPermissions(role)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
            }
        };

        await using var db = Database.CreateDbContext(context);

        return await action(new TestServiceFactory(db, context));
    }

    private async Task<T> AsPlatformAdmin<T>(Func<TestServiceFactory, Task<T>> action)
    {
        var context = TestServiceFactory.Context(null, null, Permissions.PlatformTenantsManage);

        await using var db = Database.CreateDbContext(context);

        return await action(new TestServiceFactory(db, context));
    }

    private async Task<ApprovalExecutionAuthorizationResult> AuthorizeExecutionAsync(
        int tenantId,
        int userId,
        string approvalId,
        bool forceApprovedBySelf)
    {
        await using var db = Database.CreateDbContext();

        var approval = await db.AgentApprovals.IgnoreQueryFilters()
            .SingleAsync(x => x.ApprovalId == approvalId);

        if (forceApprovedBySelf)
        {
            // What the old flow did: the requester approving their own action.
            approval.Status = AgentApprovalStatus.Approved;
            approval.DecidedByUserId = userId;
        }

        var context = TestServiceFactory.Context(tenantId, userId);
        var services = new TestServiceFactory(db, context);

        services.ContextAccessor.SetAgent(new AgentIdentity
        {
            AgentId = AgentIds.ProductAgent,
            Name = AgentIds.ProductAgent,
            Version = "1.0",
            Type = AgentType.Internal
        });

        return await services.ApprovalExecutionAuthorization.AuthorizeAsync(approval);
    }

    private async Task<int> SeedTenantAsync(string? plan = null)
    {
        await using var db = Database.CreateDbContext();

        var tenant = new Tenant
        {
            Name = "Tenant",
            Slug = $"t-{Guid.NewGuid():N}",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            Plan = plan ?? SubscriptionPlans.Professional.Id
        };

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        return tenant.Id;
    }

    private async Task<Users> SeedUsersAsync(int tenantId)
    {
        await using var db = Database.CreateDbContext();

        var requester = new User { Username = "requester", Email = "requester@example.com", IsActive = true, CreatedAtUtc = DateTime.UtcNow };
        var approver = new User { Username = "approver", Email = "approver@example.com", IsActive = true, CreatedAtUtc = DateTime.UtcNow };

        db.Users.AddRange(requester, approver);
        await db.SaveChangesAsync();

        db.TenantMemberships.AddRange(
            new TenantMembership { TenantId = tenantId, UserId = requester.Id, Role = TenantRoles.Member, IsActive = true, CreatedAtUtc = DateTime.UtcNow },
            new TenantMembership { TenantId = tenantId, UserId = approver.Id, Role = TenantRoles.Admin, IsActive = true, CreatedAtUtc = DateTime.UtcNow });

        await db.SaveChangesAsync();

        return new Users(requester.Id, approver.Id);
    }

    private async Task<string> SeedApprovalAsync(int tenantId, int userId, AgentRiskLevel risk)
    {
        await using var db = Database.CreateDbContext();

        var approval = new AgentApproval
        {
            ApprovalId = Guid.NewGuid().ToString("N"),
            RequestId = Guid.NewGuid().ToString("N"),
            SessionId = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            AgentId = AgentIds.ProductAgent,
            ToolName = risk >= AgentRiskLevel.High ? "delete_product" : "create_product",
            ToolCallId = "call-1",
            ArgumentsJson = """{"id":1}""",
            RiskLevel = risk,
            Status = AgentApprovalStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
        };

        db.AgentApprovals.Add(approval);
        await db.SaveChangesAsync();

        return approval.ApprovalId;
    }

    private async Task<Guid> SeedDelegationAsync(int tenantId, int userId)
    {
        await using var db = Database.CreateDbContext();

        var delegation = new AgentDelegationEntity
        {
            DelegationId = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            AgentId = AgentIds.ProductAgent,
            ClientId = "mcp-client",
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30)
        };

        db.AgentDelegations.Add(delegation);
        await db.SaveChangesAsync();

        return delegation.DelegationId;
    }

    private async Task SeedAgentAsync(int tenantId, string agentId, List<string> allowedRoles)
    {
        await using var db = Database.CreateDbContext();

        db.Agents.Add(new Agent
        {
            TenantId = tenantId,
            AgentId = agentId,
            Name = agentId,
            Version = "1.0",
            IsActive = true,
            Instructions = "Test agent.",
            AllowedRoles = allowedRoles,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }

    private async Task SeedTemplateAgentAsync(
        int tenantId,
        AgentTemplate template,
        int? version = null,
        bool isActive = true,
        List<string>? roles = null,
        bool detached = false)
    {
        await using var db = Database.CreateDbContext();

        var agent = template.CreateAgent(tenantId, DateTime.UtcNow);

        agent.IsActive = isActive;
        agent.AllowedRoles = roles ?? [];

        if (version is { } v)
        {
            agent.TemplateVersion = v;
            agent.Instructions = "Old instructions.";
        }

        if (detached)
        {
            agent.TemplateId = null;
            agent.TemplateVersion = null;
            agent.Instructions = "Custom instructions.";
        }

        db.Agents.Add(agent);
        await db.SaveChangesAsync();
    }
}
