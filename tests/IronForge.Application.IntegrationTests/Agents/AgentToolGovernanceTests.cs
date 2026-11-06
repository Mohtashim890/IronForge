using System.Text.Json;
using IronForge.Application.Agents.Authorization;
using IronForge.Application.Agents.Governance;
using IronForge.Application.Agents.Models;
using IronForge.Application.Agents.Runtime;
using IronForge.Application.Auth.Models;
using IronForge.Application.Auth.Services;
using IronForge.Application.Entities;
using IronForge.Application.Gym.Common;
using IronForge.Application.IntegrationTests.Infrastructure;
using IronForge.Infrastructure.Persistence;
using IronForge.Shared.Authorization;
using IronForge.Shared.Subscriptions;

namespace IronForge.Application.IntegrationTests.Agents;

/// <summary>
/// An agent's tool calls are bounded by the user it acts for, and external
/// (MCP) agents go through the same governance as internal ones.
/// </summary>
public sealed class AgentToolGovernanceTests
    : IntegrationTestBase,
      IClassFixture<PostgreSqlTestDatabase>
{
    private const int UserId = 1;

    private static readonly string GymAdminId =
        AgentTemplates.GymAdmin.Definition.AgentId;

    public AgentToolGovernanceTests(
        PostgreSqlTestDatabase database) : base(database)
    {
    }

    // ---- 1. Internal agents never exceed the user's permissions ------------

    [Theory]
    [InlineData(AgentGovernanceExecutionMode.Normal)]
    [InlineData(AgentGovernanceExecutionMode.ApprovedExecution)]
    public async Task Governance_DeniesTool_WhenTheUserLacksItsPermission(
        AgentGovernanceExecutionMode mode)
    {
        // The Gym Admin Agent holds gym.billing.manage; a Trainer does not.
        var tenantId = await SeedTenantWithGymAdminAsync();

        var decision = await AsInternalAgent(tenantId, TenantRoles.Trainer, async s =>
        {
            var definition = await s.AgentDefinitions.GetRequiredAsync(GymAdminId);

            return await s.Governance.EvaluateAsync(
                Identity(AgentType.Internal),
                s.ToolRegistry.GetEffectivePolicy(definition, "send_payment_reminder"),
                new AgentGovernanceContext
                {
                    ToolName = "send_payment_reminder",
                    Arguments = new Dictionary<string, object?> { ["invoiceId"] = 1 },
                    ExecutionMode = mode
                });
        });

        Assert.Equal(AgentGovernanceDecisionType.Deny, decision.Decision);
        Assert.Contains("You do not have permission 'gym.billing.manage'", decision.Reason);
    }

    [Fact]
    public async Task Governance_ForAdmin_AllowsReadsAndRequiresApprovalForActions()
    {
        var tenantId = await SeedTenantWithGymAdminAsync();

        var (read, write) = await AsInternalAgent(tenantId, TenantRoles.Admin, async s =>
        {
            var definition = await s.AgentDefinitions.GetRequiredAsync(GymAdminId);

            var readDecision = await s.Governance.EvaluateAsync(
                Identity(AgentType.Internal),
                s.ToolRegistry.GetEffectivePolicy(definition, "search_members"),
                new AgentGovernanceContext { ToolName = "search_members" });

            var writeDecision = await s.Governance.EvaluateAsync(
                Identity(AgentType.Internal),
                s.ToolRegistry.GetEffectivePolicy(definition, "send_payment_reminder"),
                new AgentGovernanceContext
                {
                    ToolName = "send_payment_reminder",
                    Arguments = new Dictionary<string, object?> { ["invoiceId"] = 1 }
                });

            return (readDecision, writeDecision);
        });

        Assert.Equal(AgentGovernanceDecisionType.Allow, read.Decision);
        Assert.Equal(AgentGovernanceDecisionType.ApprovalRequired, write.Decision);
    }

    [Fact]
    public async Task Governance_CashPaymentAboveTheAgentLimit_IsDenied()
    {
        // The Gym Admin template limits record_cash_payment to 2,000.
        var tenantId = await SeedTenantWithGymAdminAsync();

        var decision = await AsInternalAgent(tenantId, TenantRoles.Admin, async s =>
        {
            var definition = await s.AgentDefinitions.GetRequiredAsync(GymAdminId);

            return await s.Governance.EvaluateAsync(
                Identity(AgentType.Internal),
                s.ToolRegistry.GetEffectivePolicy(definition, "record_cash_payment"),
                new AgentGovernanceContext
                {
                    ToolName = "record_cash_payment",
                    Arguments = new Dictionary<string, object?> { ["invoiceId"] = 1, ["amount"] = 2500m }
                });
        });

        Assert.Equal(AgentGovernanceDecisionType.Deny, decision.Decision);
        Assert.Contains("exceeds the maximum allowed amount 2000", decision.Reason);
    }

    [Theory]
    [InlineData(TenantRoles.Member, new string[0])]
    [InlineData(TenantRoles.Trainer, new[]
    {
        "get_attendance_summary", "get_dashboard_summary", "get_member_details", "get_member_routines",
        "list_exercises", "list_expiring_memberships", "search_members"
    })]
    [InlineData(TenantRoles.Admin, new[]
    {
        "freeze_membership", "get_attendance_summary", "get_dashboard_summary", "get_member_details",
        "get_member_routines", "get_revenue_summary", "list_exercises", "list_expiring_memberships", "list_overdue_invoices",
        "record_cash_payment", "search_members", "send_announcement", "send_payment_reminder"
    })]
    public async Task ModelIsOnlyOfferedToolsTheUserMayUse(string role, string[] expectedTools)
    {
        var tenantId = await SeedTenantWithGymAdminAsync();

        var offered = await AsInternalAgent(tenantId, role, async s =>
        {
            var definition = await s.AgentDefinitions.GetRequiredAsync(GymAdminId);

            return s.ToolRegistry
                .CreateModelTools(definition, Identity(AgentType.Internal))
                .Select(x => x.Name)
                .Order()
                .ToList();
        });

        Assert.Equal(expectedTools, offered);
    }

    [Fact]
    public async Task GymAccess_ForInternalAgent_AlsoRequiresTheUsersPermission()
    {
        var tenantId = await SeedTenantWithGymAdminAsync();

        var asTrainer = await AsInternalAgent(tenantId, TenantRoles.Trainer, s =>
            GymAccess(s).AuthorizeAsync(Permissions.GymBillingRead));

        var asAdmin = await AsInternalAgent(tenantId, TenantRoles.Admin, s =>
            GymAccess(s).AuthorizeAsync(Permissions.GymBillingRead));

        Assert.False(asTrainer.Allowed);
        Assert.Equal("You do not have permission for this action.", asTrainer.Reason);
        Assert.True(asAdmin.Allowed);
    }

    [Fact]
    public async Task GymAccess_ForInternalAgent_RequiresTheAgentsPermission()
    {
        // An Admin may assign routines, but the Gym Admin Agent has no routine tool.
        var tenantId = await SeedTenantWithGymAdminAsync();

        var result = await AsInternalAgent(tenantId, TenantRoles.Admin, s =>
            GymAccess(s).AuthorizeAsync(Permissions.GymRoutinesManage));

        Assert.False(result.Allowed);
        Assert.Equal("Internal agent does not have the required permission.", result.Reason);
    }

    // ---- Helpers ---------------------------------------------------------------

    private static AgentIdentity Identity(AgentType type) =>
        new()
        {
            AgentId = GymAdminId,
            Name = GymAdminId,
            Version = "1.0",
            Type = type
        };

    private static GymAccessService GymAccess(TestServiceFactory s) =>
        new(
            s.ContextAccessor,
            new UserAuthorizationService(s.ContextAccessor),
            new AgentAuthorizationService(s.AgentPermissions),
            new EfRepository<MemberProfile>(s.Db),
            s.Entitlements);

    /// <summary>A signed-in user with a tenant role, chatting with the internal agent.</summary>
    private Task<T> AsInternalAgent<T>(
        int tenantId,
        string role,
        Func<TestServiceFactory, Task<T>> action)
    {
        return RunAsync(
            tenantId,
            RolePermissions.GetPermissions(role),
            Identity(AgentType.Internal),
            action);
    }

    private async Task<T> RunAsync<T>(
        int tenantId,
        string[] permissions,
        AgentIdentity agent,
        Func<TestServiceFactory, Task<T>> action)
    {
        var context = TestServiceFactory.Context(tenantId, UserId, permissions);

        await using var db = Database.CreateDbContext(context);

        var services = new TestServiceFactory(db, context);
        services.ContextAccessor.SetAgent(agent);

        return await action(services);
    }

    private async Task<int> SeedTenantWithGymAdminAsync()
    {
        var tenantId = await SeedTenantAsync(SubscriptionPlans.Professional.Id);

        await using var db = Database.CreateDbContext();

        db.Agents.Add(AgentTemplates.GymAdmin.CreateAgent(tenantId, DateTime.UtcNow));
        await db.SaveChangesAsync();

        return tenantId;
    }

    private async Task<int> SeedTenantAsync(string plan)
    {
        await using var db = Database.CreateDbContext();

        var tenant = new Tenant
        {
            Name = "Acme Gym",
            Slug = $"acme-{Guid.NewGuid():N}",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            Plan = plan
        };

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        return tenant.Id;
    }

    private async Task SeedAgentAsync(
        int tenantId,
        string agentId,
        string[] tools)
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
}
