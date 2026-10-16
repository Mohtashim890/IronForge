using IronForge.Application.Agents.Approvals;
using IronForge.Application.Agents.Authorization;
using IronForge.Application.Agents.Governance;
using IronForge.Application.Agents.Governance.Policies;
using IronForge.Application.Agents.Runtime;
using IronForge.Application.Agents.Services;
using IronForge.Application.Agents.Tools;
using IronForge.Application.Agents.Usage;
using IronForge.Application.Auditing.AgentTools;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Products.AgentTools;
using IronForge.Application.Tenants.Subscriptions;
using IronForge.Application.Auth.Services;
using IronForge.Application.Configurations;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.Tenants.Invitations;
using IronForge.Application.Tenants.Services;
using IronForge.Infrastructure.Data;
using IronForge.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IronForge.Application.IntegrationTests.Infrastructure;

/// <summary>
/// Builds application services the way the API's DI container does, over
/// one DbContext (one HTTP request) and one execution context.
/// </summary>
public sealed class TestServiceFactory
{
    public const string AcceptUrl = "https://app.test/accept-invite";

    public TestServiceFactory(
        IronForgeDbContext db,
        IronForge.Application.Execution.ExecutionContext context)
    {
        Db = db;
        ContextAccessor = new TestExecutionContextAccessor();
        ContextAccessor.Set(context);
    }

    public IronForgeDbContext Db { get; }

    public TestExecutionContextAccessor ContextAccessor { get; }

    public AuditService Audit =>
        new(new EfRepository<AuditEvent>(Db), ContextAccessor);

    public AuthService Auth =>
        new(
            new EfRepository<User>(Db),
            new EfRepository<RefreshToken>(Db),
            new EfRepository<TenantMembership>(Db),
            new EfRepository<Tenant>(Db),
            new PasswordHasher<User>(),
            Options.Create(new JwtSettings
            {
                Key = "integration-test-signing-key-that-is-long-enough",
                Issuer = "IronForge.Tests",
                Audience = "IronForge.Tests",
                ExpirationMinutes = 15
            }),
            new RefreshTokenService(),
            Audit);

    public TenantInvitationService Invitations =>
        new(
            new TenantInvitationStore(Db),
            new EfRepository<Tenant>(Db),
            new EfRepository<User>(Db),
            new EfRepository<TenantMembership>(Db),
            new PasswordHasher<User>(),
            new EfUnitOfWork(Db),
            new UserAuthorizationService(ContextAccessor),
            ContextAccessor,
            Audit,
            Options.Create(new InvitationOptions
            {
                AcceptUrl = AcceptUrl,
                LifetimeDays = 7
            }));

    public TenantProvisioningService Provisioning =>
        new(
            new EfRepository<Tenant>(Db),
            new EfRepository<TenantMembership>(Db),
            new EfRepository<Agent>(Db),
            Invitations,
            new EfUnitOfWork(Db),
            new UserAuthorizationService(ContextAccessor),
            Audit,
            new TestModelProfiles());

    public TenantMemberService Members =>
        new(
            new EfRepository<TenantMembership>(Db),
            new EfRepository<User>(Db),
            new EfRepository<AgentDelegationEntity>(Db),
            new UserAuthorizationService(ContextAccessor),
            ContextAccessor,
            Audit);

    public TenantEntitlementService Entitlements =>
        new(new EfRepository<Tenant>(Db), ContextAccessor);

    public AgentQuotaService Quota =>
        new(
            new AgentUsageStore(Db),
            new EfRepository<Tenant>(Db),
            ContextAccessor,
            Options.Create(new AgentQuotaOptions { DefaultMonthlyTokenLimit = 1_000 }));

    public DatabaseAgentDefinitionProvider AgentDefinitions =>
        new(
            new EfRepository<Agent>(Db),
            new EfRepository<AgentToolBindingEntity>(Db),
            ToolRegistry,
            Entitlements);

    /// <summary>Quota is not used by the paths under test.</summary>
    public AgentManagementService AgentManagement =>
        new(
            new EfRepository<Agent>(Db),
            new EfRepository<AgentToolBindingEntity>(Db),
            ToolRegistry,
            new TestModelProfiles(),
            null!,
            ContextAccessor,
            new AgentManagementAuthorizationService(
                ContextAccessor,
                new UserAuthorizationService(ContextAccessor)),
            Entitlements);

    /// <summary>
    /// The real tool catalogue. Only descriptors and function shells are
    /// used, so the tools' own dependencies are not needed.
    /// </summary>
    public AgentToolRegistry ToolRegistry =>
        new(
            CatalogueTools(),
            Governance,
            new UserAuthorizationService(ContextAccessor),
            NullLogger<AgentToolRegistry>.Instance);

    /// <summary>The policies in the order the API registers them.</summary>
    public AgentGovernanceService Governance =>
        new(
        [
            new UserPermissionPolicy(new UserAuthorizationService(ContextAccessor)),
            new AgentApprovalPolicy(),
            new MaximumAmountPolicy()
        ]);

    public ApprovalQueueService ApprovalQueue =>
        new(
            new EfRepository<AgentApproval>(Db),
            new EfRepository<User>(Db),
            new EfRepository<Agent>(Db),
            new UserAuthorizationService(ContextAccessor),
            ContextAccessor,
            Audit);

    public ApprovalExecutionAuthorizationService ApprovalExecutionAuthorization =>
        new(ContextAccessor);

    public AgentTemplateRolloutService TemplateRollout =>
        new(
            new EfRepository<Tenant>(Db),
            new TemplateAgentStore(Db),
            new UserAuthorizationService(ContextAccessor),
            Audit);

    /// <summary>Agent permissions, derived from tool bindings.</summary>
    public DatabaseAgentPermissionRegistry AgentPermissions =>
        new(() => AgentDefinitions, () => ToolRegistry);

    public ExternalAgentToolService ExternalAgentTools =>
        new(
            ContextAccessor,
            AgentDefinitions,
            ToolRegistry,
            Governance,
            Entitlements);

    public static IAgentTool[] CatalogueTools() =>
    [
        new GetProductsAgentTool(null!),
        new GetProductAgentTool(null!),
        new CreateProductAgentTool(null!),
        new UpdateProductAgentTool(null!),
        new DeleteProductAgentTool(null!),
        new SearchAuditEventsAgentTool(null!, null!)
    ];

    public static IronForge.Application.Execution.ExecutionContext Context(
        int? tenantId = null,
        int? userId = null,
        params string[] permissions)
    {
        return new IronForge.Application.Execution.ExecutionContext
        {
            TenantId = tenantId,
            Source = ExecutionSource.Web,
            Actor = new ExecutionActor
            {
                UserId = userId,
                UserPermissions = permissions.ToHashSet(StringComparer.OrdinalIgnoreCase)
            }
        };
    }
}

/// <summary>The two profiles the plans use; no model is ever called.</summary>
public sealed class TestModelProfiles : IronForge.Application.Agents.ModelProfiles.IAgentChatClientFactory
{
    private static readonly IReadOnlyList<IronForge.Application.Agents.ModelProfiles.AgentModelProfile> Profiles =
    [
        new("economy", "test-economy", 4000, "Economy"),
        new("premium", "test-premium", null, "Premium (add-on only)"),
        new("standard", "test-standard", null, "Standard")
    ];

    public IReadOnlyList<IronForge.Application.Agents.ModelProfiles.AgentModelProfile> GetProfiles() => Profiles;

    public bool HasProfile(string profileName) =>
        Profiles.Any(x => x.Name == profileName);

    public (Microsoft.Extensions.AI.IChatClient Client, IronForge.Application.Agents.ModelProfiles.AgentModelProfile Profile)
        GetClient(string? profileName) =>
        throw new NotSupportedException("Tests do not call models.");
}
