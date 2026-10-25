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
using IronForge.Application.Tenants.Subscriptions;
using IronForge.Application.Auth.Services;
using IronForge.Application.Auth.TwoFactor;
using IronForge.Application.Configurations;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.Gym.Access;
using IronForge.Application.Gym.AgentTools;
using IronForge.Application.Gym.Billing;
using IronForge.Application.Gym.CheckIns;
using IronForge.Application.Gym.Common;
using IronForge.Application.Gym.Fitness;
using IronForge.Application.Gym.Members;
using IronForge.Application.Gym.Notifications;
using IronForge.Application.Gym.Settings;
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

    /// <summary>An anonymous request whose context the code under test establishes (a device).</summary>
    public TestServiceFactory(IronForgeDbContext db, TestExecutionContextAccessor accessor)
    {
        Db = db;
        ContextAccessor = accessor;
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
                Key = JwtKey,
                Issuer = "IronForge.Tests",
                Audience = "IronForge.Tests",
                ExpirationMinutes = 15
            }),
            new RefreshTokenService(),
            Audit,
            Directory,
            SignInGuard,
            TwoFactor);

    public const string JwtKey = "integration-test-signing-key-that-is-long-enough";

    public SignInGuard SignInGuard =>
        new(new EfRepository<User>(Db), new PasswordHasher<User>(), Audit, Clock);

    /// <summary>
    /// Two-factor is not required, so tests of other features sign admins in
    /// with a password; the two-factor tests turn it on as in production.
    /// </summary>
    public TwoFactorOptions TwoFactorOptions { get; init; } = new()
    {
        EncryptionKey = "integration-test-two-factor-key-long-enough",
        RequiredForAdmins = false
    };

    public TwoFactorSecretProtector TwoFactorProtector => new(Options.Create(TwoFactorOptions));

    public TwoFactorService TwoFactor =>
        new(
            new EfRepository<User>(Db),
            new EfRepository<TenantMembership>(Db),
            TwoFactorProtector,
            SignInGuard,
            ContextAccessor,
            Audit,
            Options.Create(TwoFactorOptions),
            Clock);

    /// <summary>One cache per factory, like the API's singleton.</summary>
    public SessionStateCache SessionCache { get; init; } = new();

    public SessionValidator Sessions =>
        new(
            new EfRepository<User>(Db),
            new EfRepository<TenantMembership>(Db),
            new EfRepository<Tenant>(Db),
            SessionCache,
            Clock);

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
            }),
            new EfRepository<MemberProfile>(Db),
            Directory,
            Notifications,
            SignInGuard);

    public TenantProvisioningService Provisioning =>
        new(
            new EfRepository<Tenant>(Db),
            new EfRepository<TenantMembership>(Db),
            new EfRepository<Agent>(Db),
            Invitations,
            new EfUnitOfWork(Db),
            new UserAuthorizationService(ContextAccessor),
            Audit,
            new TestModelProfiles(),
            GymSettingsFactory,
            TemplateRollout);

    public TenantMemberService Members =>
        new(
            new EfRepository<TenantMembership>(Db),
            new EfRepository<User>(Db),
            new EfRepository<AgentDelegationEntity>(Db),
            new UserAuthorizationService(ContextAccessor),
            ContextAccessor,
            Audit,
            new EfRepository<RefreshToken>(Db),
            SessionCache);

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

    /// <summary>The real catalogue: every gym tool plus the audit tool.</summary>
    public static IAgentTool[] CatalogueTools() =>
        GymAgentToolCatalog.Types
            .Select(type =>
            {
                var constructor = type.GetConstructors().Single();

                return (IAgentTool)constructor.Invoke(
                    constructor.GetParameters().Select(_ => (object?)null).ToArray());
            })
            .Append(new SearchAuditEventsAgentTool(null!, null!))
            .ToArray();

    // ---- Gym services --------------------------------------------------------

    /// <summary>The clock gym services use; tests may move it.</summary>
    public TimeProvider Clock { get; set; } = TimeProvider.System;

    public GymDirectory Directory => new(Db);

    public NotificationService Notifications =>
        new(
            new EfRepository<Notification>(Db),
            new EfRepository<EmailOutboxMessage>(Db),
            ContextAccessor,
            Clock);

    public GymSettingsFactory GymSettingsFactory =>
        new(new EfRepository<GymSettings>(Db), Clock);

    /// <summary>Agent permission checks are real; external-agent delegation is not used by these paths.</summary>
    public GymAccessService GymAccess =>
        new(
            ContextAccessor,
            new UserAuthorizationService(ContextAccessor),
            new IronForge.Application.Agents.Authorization.AgentAuthorizationService(AgentPermissions),
            null!,
            new EfRepository<MemberProfile>(Db),
            Entitlements);

    public BillingEngine BillingEngine =>
        new(
            new EfRepository<Tenant>(Db),
            new EfRepository<GymSettings>(Db),
            new EfRepository<MemberProfile>(Db),
            new EfRepository<MembershipPlan>(Db),
            new EfRepository<Invoice>(Db),
            new EfRepository<Payment>(Db),
            new EfRepository<InvoiceReminder>(Db),
            new InvoiceNumberGenerator(Db),
            Notifications,
            Clock);

    public TestPaymentGateway PaymentGateway { get; } = new();

    public BillingService Billing =>
        new(
            GymAccess,
            new EfRepository<Invoice>(Db),
            new EfRepository<Payment>(Db),
            new EfRepository<MemberProfile>(Db),
            BillingEngine,
            PaymentGateway,
            Audit,
            Clock);

    public PaymentConfirmationService PaymentConfirmation =>
        new(
            new EfRepository<ProcessedWebhookEvent>(Db),
            new EfRepository<Invoice>(Db),
            new EfRepository<MemberProfile>(Db),
            BillingEngine,
            PaymentGateway,
            new EfUnitOfWork(Db),
            Audit,
            Clock,
            NullLogger<PaymentConfirmationService>.Instance);

    public MembershipPlanService Plans =>
        new(
            GymAccess,
            new EfRepository<MembershipPlan>(Db),
            new EfRepository<MemberProfile>(Db),
            new EfRepository<GymSettings>(Db),
            Audit,
            Clock);

    public MemberService GymMembers =>
        new(
            GymAccess,
            new EfRepository<MemberProfile>(Db),
            new EfRepository<MembershipPlan>(Db),
            new EfRepository<Invoice>(Db),
            new EfRepository<CheckIn>(Db),
            new EfRepository<WorkoutRoutine>(Db),
            new EfRepository<WorkoutLog>(Db),
            new EfRepository<BodyMetric>(Db),
            new EfRepository<FitnessGoal>(Db),
            new EfRepository<MemberPhoto>(Db),
            new EfRepository<TenantMembership>(Db),
            new EfRepository<User>(Db),
            BillingEngine,
            Invitations,
            Entitlements,
            Notifications,
            new EfUnitOfWork(Db),
            Audit,
            Clock,
            PaymentGateway);

    public const string PassSigningKey = "integration-test-member-pass-key-long-enough";

    public static CheckInKeys PassKeys { get; } =
        new(Options.Create(new CheckInOptions { PassSigningKey = PassSigningKey }));

    public CheckInService CheckIns =>
        new(
            GymAccess,
            new EfRepository<CheckIn>(Db),
            new EfRepository<CheckInDenial>(Db),
            new EfRepository<MemberProfile>(Db),
            new EfRepository<Invoice>(Db),
            new EfRepository<MemberPhoto>(Db),
            new EfRepository<CheckInDevice>(Db),
            PassKeys,
            BillingEngine,
            Audit,
            Clock);

    public GymSettingsService GymSettings =>
        new(
            GymAccess,
            new EfRepository<GymSettings>(Db),
            new EfRepository<Tenant>(Db),
            PaymentGateway,
            Audit,
            Clock);

    public MemberPassService MemberPasses =>
        new(
            GymAccess,
            new EfRepository<MemberProfile>(Db),
            new EfRepository<MemberPhoto>(Db),
            PassKeys,
            BillingEngine,
            Audit,
            Notifications,
            Clock);

    public MemberPhotoService MemberPhotos =>
        new(
            GymAccess,
            new EfRepository<MemberProfile>(Db),
            new EfRepository<MemberPhoto>(Db),
            Audit,
            Clock);

    public CheckInDeviceService CheckInDevices =>
        new(
            GymAccess,
            new EfRepository<CheckInDevice>(Db),
            new EfRepository<CheckIn>(Db),
            new EfRepository<TerminalUserSync>(Db),
            BillingEngine,
            Audit,
            Clock);

    private TerminalProtocolService? _terminal;

    /// <summary>One per request, like the API's scope: it remembers the authenticated terminal.</summary>
    public TerminalProtocolService Terminal => _terminal ??=
        new(
            new CheckInDeviceDirectory(Db),
            ContextAccessor,
            GymAccess,
            new EfRepository<Tenant>(Db),
            new EfRepository<CheckInDevice>(Db),
            new EfRepository<TerminalUser>(Db),
            new EfRepository<TerminalUserSync>(Db),
            new EfRepository<TerminalCommand>(Db),
            new EfRepository<MemberProfile>(Db),
            new EfRepository<Invoice>(Db),
            new EfRepository<CheckIn>(Db),
            BillingEngine,
            Audit,
            Clock);

    public TerminalAdminService TerminalAdmin =>
        new(
            GymAccess,
            new CheckInDeviceDirectory(Db),
            new EfRepository<CheckInDevice>(Db),
            new EfRepository<TerminalUser>(Db),
            new EfRepository<TerminalUserSync>(Db),
            new EfRepository<TerminalCommand>(Db),
            new EfRepository<MemberProfile>(Db),
            Audit,
            Clock);

    public KioskService Kiosk =>
        new(
            new CheckInDeviceDirectory(Db),
            new EfRepository<Tenant>(Db),
            new EfRepository<GymSettings>(Db),
            ContextAccessor,
            Audit,
            Clock);

    public GymJoinService Join =>
        new(
            new EfRepository<GymSettings>(Db),
            new EfRepository<Tenant>(Db),
            new EfRepository<User>(Db),
            new EfRepository<TenantMembership>(Db),
            new EfRepository<MemberProfile>(Db),
            Directory,
            new PasswordHasher<User>(),
            Notifications,
            new EfUnitOfWork(Db),
            Audit,
            Clock,
            Invitations,
            SignInGuard);

    public RoutineService Routines =>
        new(
            GymAccess,
            new EfRepository<Exercise>(Db),
            new EfRepository<WorkoutRoutine>(Db),
            new EfRepository<MemberProfile>(Db),
            new EfRepository<User>(Db),
            BillingEngine,
            Notifications,
            Audit,
            Clock);

    public ProgressService Progress =>
        new(
            GymAccess,
            new EfRepository<WorkoutLog>(Db),
            new EfRepository<WorkoutRoutine>(Db),
            new EfRepository<RoutineDay>(Db),
            new EfRepository<Exercise>(Db),
            new EfRepository<BodyMetric>(Db),
            new EfRepository<FitnessGoal>(Db),
            new EfRepository<MemberProfile>(Db),
            BillingEngine,
            Clock);

    public AnnouncementService Announcements =>
        new(
            GymAccess,
            new EfRepository<Announcement>(Db),
            new EfRepository<MemberProfile>(Db),
            new EfRepository<MembershipPlan>(Db),
            BillingEngine,
            Notifications,
            Audit,
            Clock);

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
                Username = userId is null ? null : $"user{userId}",
                UserPermissions = permissions.ToHashSet(StringComparer.OrdinalIgnoreCase)
            }
        };
    }

    /// <summary>A signed-in user acting in a gym with a tenant role.</summary>
    public static IronForge.Application.Execution.ExecutionContext AsRole(
        int tenantId,
        int userId,
        string role)
    {
        return new IronForge.Application.Execution.ExecutionContext
        {
            TenantId = tenantId,
            Source = ExecutionSource.Web,
            Actor = new ExecutionActor
            {
                UserId = userId,
                Username = $"user{userId}",
                Role = role,
                UserPermissions = IronForge.Application.Auth.Models.RolePermissions
                    .GetPermissions(role)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
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

/// <summary>Checkout without a payment provider: sessions are recorded, nothing is charged.</summary>
public sealed class TestPaymentGateway : IPaymentGateway
{
    public List<CheckoutRequest> Checkouts { get; } = [];

    public string Provider => "test";

    public bool IsSimulated => true;

    /// <summary>Tests turn it off to act like a platform without an online payment provider.</summary>
    public bool TakesPayments { get; set; } = true;

    public bool SupportsStripeConnect => false;

    public bool CanTakePayments(GymSettings settings) => TakesPayments;

    public Task<CheckoutResult> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken = default)
    {
        Checkouts.Add(request);

        var session = $"test_{Guid.NewGuid():N}";

        return Task.FromResult(new CheckoutResult(session, $"https://pay.test/{session}"));
    }

    public Task<ConnectOnboardingLink> CreateConnectOnboardingLinkAsync(
        string? existingAccountId,
        string? email,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<bool> IsChargesEnabledAsync(string accountId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

/// <summary>A clock tests can set and move.</summary>
public sealed class TestClock : TimeProvider
{
    public TestClock(DateTimeOffset now) => Now = now;

    public DateTimeOffset Now { get; set; }

    public override DateTimeOffset GetUtcNow() => Now;
}
