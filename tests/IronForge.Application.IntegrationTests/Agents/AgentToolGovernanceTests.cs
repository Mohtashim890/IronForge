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
        "get_attendance_summary", "get_dashboard_summary", "get_member_details",
        "list_expiring_memberships", "search_members"
    })]
    [InlineData(TenantRoles.Admin, new[]
    {
        "freeze_membership", "get_attendance_summary", "get_dashboard_summary", "get_member_details",
        "get_revenue_summary", "list_expiring_memberships", "list_overdue_invoices",
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

    // ---- 2. MCP calls go through the same governance -------------------------

    [Fact]
    public async Task External_ReadToolBoundToTheAgent_IsAllowed()
    {
        var tenantId = await SeedTenantWithGymAdminAsync();

        var result = await AsExternalAgent(tenantId, [Permissions.GymMembersRead], s =>
            s.ExternalAgentTools.AuthorizeAsync(
                "search_members",
                new Dictionary<string, object?>()));

        Assert.True(result.Allowed, result.Reason);
    }

    [Fact]
    public async Task External_ToolThatNeedsApproval_IsRefused()
    {
        var tenantId = await SeedTenantWithGymAdminAsync();

        var result = await AsExternalAgent(tenantId, [Permissions.GymBillingManage], s =>
            s.ExternalAgentTools.AuthorizeAsync(
                "send_payment_reminder",
                McpArguments("""{ "invoiceId": 1 }""")));

        Assert.False(result.Allowed);
        Assert.Contains("requires human approval, which is not available over MCP", result.Reason);
    }

    [Fact]
    public async Task External_AmountLimit_AppliesToMcpArguments()
    {
        var tenantId = await SeedTenantWithGymAdminAsync();

        var result = await AsExternalAgent(tenantId, [Permissions.GymBillingManage], s =>
            s.ExternalAgentTools.AuthorizeAsync(
                "record_cash_payment",
                McpArguments("""{ "invoiceId": 1, "amount": 50000 }""")));

        Assert.False(result.Allowed);
        Assert.Contains("exceeds the maximum allowed amount 2000", result.Reason);
    }

    [Fact]
    public async Task External_ToolNotBoundToTheAgent_IsRefused()
    {
        var tenantId = await SeedTenantAsync(SubscriptionPlans.Professional.Id);
        await SeedAgentAsync(tenantId, GymAdminId, ["search_members"]);

        var result = await AsExternalAgent(tenantId, [Permissions.GymMembersRead], s =>
            s.ExternalAgentTools.AuthorizeAsync(
                "get_member_details",
                McpArguments("""{ "memberId": 1 }""")));

        Assert.False(result.Allowed);
        Assert.Equal($"Tool 'get_member_details' is not enabled for agent '{GymAdminId}'.", result.Reason);
    }

    [Fact]
    public async Task External_ToolOfDisabledModule_IsRefused()
    {
        var tenantId = await SeedTenantAsync(SubscriptionPlans.Starter.Id);
        await SeedAgentAsync(tenantId, GymAdminId, ["search_audit_events"]);

        var result = await AsExternalAgent(tenantId, [Permissions.AuditRead], s =>
            s.ExternalAgentTools.AuthorizeAsync(
                "search_audit_events",
                new Dictionary<string, object?>()));

        Assert.False(result.Allowed);
        Assert.Contains("not included in this workspace's subscription", result.Reason);
    }

    [Fact]
    public async Task External_ScopeNotDelegated_IsDeniedByTheUserPolicy()
    {
        var tenantId = await SeedTenantWithGymAdminAsync();

        // Only gym.members.read was delegated; the agent itself may freeze.
        var result = await AsExternalAgent(tenantId, [Permissions.GymMembersRead], s =>
            s.ExternalAgentTools.AuthorizeAsync(
                "freeze_membership",
                McpArguments("""{ "memberId": 1, "until": "2030-01-01" }""")));

        Assert.False(result.Allowed);
        Assert.Contains("You do not have permission 'gym.members.manage'", result.Reason);
    }

    [Fact]
    public async Task External_UnknownTool_IsRefused()
    {
        var tenantId = await SeedTenantWithGymAdminAsync();

        var result = await AsExternalAgent(tenantId, [Permissions.GymMembersRead], s =>
            s.ExternalAgentTools.AuthorizeAsync(
                "drop_database",
                new Dictionary<string, object?>()));

        Assert.False(result.Allowed);
        Assert.Equal("Tool 'drop_database' is not in the tool catalogue.", result.Reason);
    }

    [Theory]
    [InlineData(new[] { "gym.members.read" }, new[]
    {
        "get_attendance_summary", "get_dashboard_summary", "get_member_details",
        "list_expiring_memberships", "search_members"
    })]
    // Reminders and cash payments need approval, which MCP cannot provide.
    [InlineData(new[] { "gym.billing.read", "gym.billing.manage" }, new[]
    {
        "get_revenue_summary", "list_overdue_invoices"
    })]
    [InlineData(new string[0], new string[0])]
    public async Task External_ListsOnlyToolsItCanActuallyCall(string[] scopes, string[] expected)
    {
        var tenantId = await SeedTenantWithGymAdminAsync();

        var tools = await AsExternalAgent(tenantId, scopes, async s =>
            (await s.ExternalAgentTools.GetCallableToolsAsync())
                .Select(x => x.Name)
                .Order()
                .ToList());

        Assert.Equal(expected, tools);
    }

    [Fact]
    public async Task External_ToolSchemaIsTheCatalogueSchema()
    {
        var tenantId = await SeedTenantWithGymAdminAsync();

        var details = await AsExternalAgent(tenantId, [Permissions.GymMembersRead], async s =>
            (await s.ExternalAgentTools.GetCallableToolsAsync())
                .Single(x => x.Name == "get_member_details"));

        Assert.True(details.JsonSchema.GetProperty("properties").TryGetProperty("memberId", out _));
    }

    [Fact]
    public async Task External_InvokingAToolThatIsNotCallable_IsRefused()
    {
        var tenantId = await SeedTenantWithGymAdminAsync();

        var (success, _, error) = await AsExternalAgent(tenantId, [Permissions.GymBillingManage], s =>
            s.ExternalAgentTools.InvokeAsync(
                "send_payment_reminder",
                McpArguments("""{ "invoiceId": 1 }""")));

        Assert.False(success);
        Assert.Equal("Tool 'send_payment_reminder' is not available to this agent.", error);
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

    /// <summary>Arguments as the MCP server passes them: JSON values by name.</summary>
    private static IDictionary<string, object?> McpArguments(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!
            .ToDictionary(x => x.Key, x => (object?)x.Value);

    private static GymAccessService GymAccess(TestServiceFactory s) =>
        new(
            s.ContextAccessor,
            new UserAuthorizationService(s.ContextAccessor),
            new AgentAuthorizationService(s.AgentPermissions),
            effectiveAgentAuthorization: null!,
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

    /// <summary>An MCP call: the credential carries only the delegated scopes.</summary>
    private Task<T> AsExternalAgent<T>(
        int tenantId,
        string[] delegatedScopes,
        Func<TestServiceFactory, Task<T>> action)
    {
        return RunAsync(
            tenantId,
            delegatedScopes,
            Identity(AgentType.External),
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
