using System.Text.Json;
using IronForge.Application.Agents.Governance;
using IronForge.Application.Agents.Models;
using IronForge.Application.Agents.Runtime;
using IronForge.Application.Auth.Models;
using IronForge.Application.Entities;
using IronForge.Application.IntegrationTests.Infrastructure;
using IronForge.Application.Agents.Authorization;
using IronForge.Application.Auth.Services;
using IronForge.Application.Products.Services;
using IronForge.Application.Tenants.Services;
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

    private static readonly string ProductAgentId =
        AgentTemplates.ProductAgent.Definition.AgentId;

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
        // The Product Agent holds products.create; a Viewer does not.
        var tenantId = await SeedTenantWithProductAgentAsync();

        var decision = await AsInternalAgent(tenantId, TenantRoles.Viewer, async s =>
        {
            var definition = await s.AgentDefinitions.GetRequiredAsync(ProductAgentId);

            return await s.Governance.EvaluateAsync(
                Identity(AgentType.Internal),
                s.ToolRegistry.GetEffectivePolicy(definition, "create_product"),
                new AgentGovernanceContext
                {
                    ToolName = "create_product",
                    Arguments = new Dictionary<string, object?> { ["price"] = 10m },
                    ExecutionMode = mode
                });
        });

        Assert.Equal(AgentGovernanceDecisionType.Deny, decision.Decision);
        Assert.Contains("You do not have permission 'products.create'", decision.Reason);
    }

    [Fact]
    public async Task Governance_ForMember_AllowsReadsAndRequiresApprovalForWrites()
    {
        var tenantId = await SeedTenantWithProductAgentAsync();

        var (read, write) = await AsInternalAgent(tenantId, TenantRoles.Member, async s =>
        {
            var definition = await s.AgentDefinitions.GetRequiredAsync(ProductAgentId);

            var readDecision = await s.Governance.EvaluateAsync(
                Identity(AgentType.Internal),
                s.ToolRegistry.GetEffectivePolicy(definition, "get_products"),
                new AgentGovernanceContext { ToolName = "get_products" });

            var writeDecision = await s.Governance.EvaluateAsync(
                Identity(AgentType.Internal),
                s.ToolRegistry.GetEffectivePolicy(definition, "create_product"),
                new AgentGovernanceContext
                {
                    ToolName = "create_product",
                    Arguments = new Dictionary<string, object?> { ["price"] = 10m }
                });

            return (readDecision, writeDecision);
        });

        Assert.Equal(AgentGovernanceDecisionType.Allow, read.Decision);
        Assert.Equal(AgentGovernanceDecisionType.ApprovalRequired, write.Decision);
    }

    [Theory]
    [InlineData(TenantRoles.Viewer, new[] { "get_product", "get_products" })]
    [InlineData(TenantRoles.Member, new[] { "create_product", "get_product", "get_products", "update_product" })]
    [InlineData(TenantRoles.Admin, new[] { "create_product", "delete_product", "get_product", "get_products", "update_product" })]
    public async Task ModelIsOnlyOfferedToolsTheUserMayUse(string role, string[] expectedTools)
    {
        var tenantId = await SeedTenantWithProductAgentAsync();

        var offered = await AsInternalAgent(tenantId, role, async s =>
        {
            var definition = await s.AgentDefinitions.GetRequiredAsync(ProductAgentId);

            return s.ToolRegistry
                .CreateModelTools(definition, Identity(AgentType.Internal))
                .Select(x => x.Name)
                .Order()
                .ToList();
        });

        Assert.Equal(expectedTools, offered);
    }

    [Fact]
    public async Task ProductCollectionAccess_ForInternalAgent_AlsoRequiresTheUsersPermission()
    {
        var tenantId = await SeedTenantWithProductAgentAsync();

        var asViewer = await AsInternalAgent(tenantId, TenantRoles.Viewer, s =>
            ProductAuthorization(s).AuthorizeCollectionAsync(Permissions.ProductsCreate));

        var asMember = await AsInternalAgent(tenantId, TenantRoles.Member, s =>
            ProductAuthorization(s).AuthorizeCollectionAsync(Permissions.ProductsCreate));

        Assert.False(asViewer.Allowed);
        Assert.Equal("User does not have the required permission.", asViewer.Reason);
        Assert.True(asMember.Allowed);
    }

    // ---- 2. MCP calls go through the same governance -------------------------

    [Fact]
    public async Task External_ReadToolBoundToTheAgent_IsAllowed()
    {
        var tenantId = await SeedTenantWithProductAgentAsync();

        var result = await AsExternalAgent(tenantId, [Permissions.ProductsRead], s =>
            s.ExternalAgentTools.AuthorizeAsync(
                "get_products",
                new Dictionary<string, object?>()));

        Assert.True(result.Allowed, result.Reason);
    }

    [Fact]
    public async Task External_ToolThatNeedsApproval_IsRefused()
    {
        var tenantId = await SeedTenantWithProductAgentAsync();

        var result = await AsExternalAgent(tenantId, [Permissions.ProductsCreate], s =>
            s.ExternalAgentTools.AuthorizeAsync(
                "create_product",
                McpArguments("""{ "name": "Desk", "price": 10 }""")));

        Assert.False(result.Allowed);
        Assert.Contains("requires human approval, which is not available over MCP", result.Reason);
    }

    [Fact]
    public async Task External_AmountLimit_AppliesToMcpArguments()
    {
        // The Product Agent template limits create_product to 10,000.
        var tenantId = await SeedTenantWithProductAgentAsync();

        var result = await AsExternalAgent(tenantId, [Permissions.ProductsCreate], s =>
            s.ExternalAgentTools.AuthorizeAsync(
                "create_product",
                McpArguments("""{ "name": "Server rack", "price": 50000 }""")));

        Assert.False(result.Allowed);
        Assert.Contains("exceeds the maximum allowed price 10000", result.Reason);
    }

    [Fact]
    public async Task External_ToolNotBoundToTheAgent_IsRefused()
    {
        var tenantId = await SeedTenantAsync(SubscriptionPlans.Professional.Id);
        await SeedAgentAsync(tenantId, ProductAgentId, ["get_products"]);

        var result = await AsExternalAgent(tenantId, [Permissions.ProductsRead], s =>
            s.ExternalAgentTools.AuthorizeAsync(
                "get_product",
                McpArguments("""{ "id": 1 }""")));

        Assert.False(result.Allowed);
        Assert.Equal($"Tool 'get_product' is not enabled for agent '{ProductAgentId}'.", result.Reason);
    }

    [Fact]
    public async Task External_ToolOfDisabledModule_IsRefused()
    {
        var tenantId = await SeedTenantAsync(SubscriptionPlans.Starter.Id);
        await SeedAgentAsync(tenantId, ProductAgentId, ["search_audit_events"]);

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
        var tenantId = await SeedTenantWithProductAgentAsync();

        // Only products.read was delegated; the agent itself may delete.
        var result = await AsExternalAgent(tenantId, [Permissions.ProductsRead], s =>
            s.ExternalAgentTools.AuthorizeAsync(
                "delete_product",
                McpArguments("""{ "id": 1 }""")));

        Assert.False(result.Allowed);
        Assert.Contains("You do not have permission 'products.delete'", result.Reason);
    }

    [Fact]
    public async Task External_UnknownTool_IsRefused()
    {
        var tenantId = await SeedTenantWithProductAgentAsync();

        var result = await AsExternalAgent(tenantId, [Permissions.ProductsRead], s =>
            s.ExternalAgentTools.AuthorizeAsync(
                "drop_database",
                new Dictionary<string, object?>()));

        Assert.False(result.Allowed);
        Assert.Equal("Tool 'drop_database' is not in the tool catalogue.", result.Reason);
    }

    [Theory]
    [InlineData(new[] { "products.read" }, new[] { "get_product", "get_products" })]
    // create/delete need approval, which MCP cannot provide; update was not delegated.
    [InlineData(new[] { "products.read", "products.create", "products.delete" }, new[] { "get_product", "get_products" })]
    [InlineData(new string[0], new string[0])]
    public async Task External_ListsOnlyToolsItCanActuallyCall(string[] scopes, string[] expected)
    {
        var tenantId = await SeedTenantWithProductAgentAsync();

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
        var tenantId = await SeedTenantWithProductAgentAsync();

        var getProduct = await AsExternalAgent(tenantId, [Permissions.ProductsRead], async s =>
            (await s.ExternalAgentTools.GetCallableToolsAsync())
                .Single(x => x.Name == "get_product"));

        Assert.True(getProduct.JsonSchema.GetProperty("properties").TryGetProperty("id", out _));
    }

    [Fact]
    public async Task External_InvokingAToolThatIsNotCallable_IsRefused()
    {
        var tenantId = await SeedTenantWithProductAgentAsync();

        var (success, _, error) = await AsExternalAgent(tenantId, [Permissions.ProductsCreate], s =>
            s.ExternalAgentTools.InvokeAsync(
                "create_product",
                McpArguments("""{ "name": "Desk", "price": 10 }""")));

        Assert.False(success);
        Assert.Equal("Tool 'create_product' is not available to this agent.", error);
    }

    // ---- Helpers ---------------------------------------------------------------

    private static AgentIdentity Identity(AgentType type) =>
        new()
        {
            AgentId = ProductAgentId,
            Name = ProductAgentId,
            Version = "1.0",
            Type = type
        };

    /// <summary>Arguments as the MCP server passes them: JSON values by name.</summary>
    private static IDictionary<string, object?> McpArguments(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!
            .ToDictionary(x => x.Key, x => (object?)x.Value);

    private static ProductAuthorizationService ProductAuthorization(TestServiceFactory s)
    {
        var userAuthorization = new UserAuthorizationService(s.ContextAccessor);

        return new ProductAuthorizationService(
            s.ContextAccessor,
            userAuthorization,
            effectiveAgentAuthorization: null!,
            new AgentAuthorizationService(s.AgentPermissions),
            new TenantAuthorizationService(s.ContextAccessor));
    }

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

    private async Task<int> SeedTenantWithProductAgentAsync()
    {
        var tenantId = await SeedTenantAsync(SubscriptionPlans.Professional.Id);

        await using var db = Database.CreateDbContext();

        db.Agents.Add(AgentTemplates.ProductAgent.CreateAgent(tenantId, DateTime.UtcNow));
        await db.SaveChangesAsync();

        return tenantId;
    }

    private async Task<int> SeedTenantAsync(string plan)
    {
        await using var db = Database.CreateDbContext();

        var tenant = new Tenant
        {
            Name = "Acme",
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
