using IronForge.Application.Agents.ModelProfiles;
using IronForge.Application.Agents.Runtime;
using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Auth.Services;
using IronForge.Application.Commons;
using IronForge.Application.Entities;
using IronForge.Application.Persistence;
using IronForge.Application.Tenants.Invitations;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Tenants;
using IronForge.Shared.Subscriptions;

namespace IronForge.Application.Tenants.Services;

/// <summary>
/// Platform-operator operations on tenants. All of them require the
/// platform.tenants.manage permission and work without a selected tenant.
/// </summary>
public interface ITenantProvisioningService
{
    Task<ServiceResult<List<PlatformTenantDto>>> ListTenantsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Onboards a client in one transaction: creates the tenant on its plan,
    /// installs the <see cref="AgentTemplates.Default"/> agents the plan covers
    /// and invites its first Admin.
    /// </summary>
    Task<ServiceResult<ProvisionTenantResult>> ProvisionAsync(
        ProvisionTenantRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<PlatformTenantDto>> SetStatusAsync(
        int tenantId,
        bool isActive,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<TenantInvitationLinkDto>> InviteAdminAsync(
        int tenantId,
        string email,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes plan, subscription status, add-on modules and token limit.
    /// Takes effect on the tenant's next request: the tool catalogue and agent
    /// definitions are read per request, access at the next token refresh.
    /// </summary>
    Task<ServiceResult<PlatformTenantDto>> UpdateSubscriptionAsync(
        int tenantId,
        UpdateTenantSubscriptionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Every configured model profile, for choosing add-ons.</summary>
    ServiceResult<List<string>> ListModelProfiles();
}

public sealed class TenantProvisioningService : ITenantProvisioningService
{
    private readonly IRepository<Tenant> _tenants;
    private readonly IRepository<TenantMembership> _memberships;
    private readonly IRepository<Agent> _agents;
    private readonly ITenantInvitationService _invitations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserAuthorizationService _authorization;
    private readonly IAuditService _auditService;
    private readonly IAgentChatClientFactory _modelProfiles;

    public TenantProvisioningService(
        IRepository<Tenant> tenants,
        IRepository<TenantMembership> memberships,
        IRepository<Agent> agents,
        ITenantInvitationService invitations,
        IUnitOfWork unitOfWork,
        IUserAuthorizationService authorization,
        IAuditService auditService,
        IAgentChatClientFactory modelProfiles)
    {
        _tenants = tenants;
        _memberships = memberships;
        _agents = agents;
        _invitations = invitations;
        _unitOfWork = unitOfWork;
        _authorization = authorization;
        _auditService = auditService;
        _modelProfiles = modelProfiles;
    }

    public async Task<ServiceResult<List<PlatformTenantDto>>> ListTenantsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!IsPlatformAdmin())
        {
            return Forbidden<List<PlatformTenantDto>>();
        }

        var tenants = await _tenants.ListAsync(cancellationToken);

        var activeMemberships = await _memberships.ListAsync(
            x => x.IsActive,
            cancellationToken);

        var memberCounts = activeMemberships
            .GroupBy(x => x.TenantId)
            .ToDictionary(x => x.Key, x => x.Count());

        return ServiceResult<List<PlatformTenantDto>>.Ok(
            tenants
                .OrderBy(x => x.Name)
                .Select(x => Map(x, memberCounts.GetValueOrDefault(x.Id)))
                .ToList());
    }

    public async Task<ServiceResult<ProvisionTenantResult>> ProvisionAsync(
        ProvisionTenantRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsPlatformAdmin())
        {
            return Forbidden<ProvisionTenantResult>();
        }

        var name = request.Name.Trim();
        var slug = request.Slug.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(slug))
        {
            return ServiceResult<ProvisionTenantResult>.Fail(
                "Tenant name and slug are required.",
                400);
        }

        if (SubscriptionPlans.Find(request.Plan) is null)
        {
            return ServiceResult<ProvisionTenantResult>.Fail(
                UnknownPlanMessage(request.Plan),
                400);
        }

        if (await _tenants.AnyAsync(x => x.Slug == slug, cancellationToken))
        {
            return ServiceResult<ProvisionTenantResult>.Fail(
                $"A tenant with the slug '{slug}' already exists.",
                409);
        }

        ProvisionTenantResult provisioned;

        try
        {
            provisioned = await _unitOfWork.ExecuteInTransactionAsync(
                () => CreateTenantAsync(name, slug, request, cancellationToken),
                cancellationToken);
        }
        catch (ProvisioningFailedException failure)
        {
            return ServiceResult<ProvisionTenantResult>.Fail(
                failure.Message,
                failure.StatusCode ?? 400);
        }

        await _auditService.RecordAsync(
            AuditCategory.Tenant,
            "TenantProvisioned",
            AuditOutcome.Succeeded,
            actorType: AuditActorType.User,
            resourceType: "Tenant",
            resourceId: provisioned.Tenant.TenantId.ToString(),
            reason: $"Provisioned '{provisioned.Tenant.Slug}' on plan '{provisioned.Tenant.Plan}'.",
            cancellationToken: cancellationToken,
            tenantId: provisioned.Tenant.TenantId);

        return ServiceResult<ProvisionTenantResult>.Ok(provisioned);
    }

    private async Task<ProvisionTenantResult> CreateTenantAsync(
        string name,
        string slug,
        ProvisionTenantRequest request,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var tenant = new Tenant
        {
            Name = name,
            Slug = slug,
            IsActive = true,
            CreatedAtUtc = now,
            MonthlyAgentTokenLimit = request.MonthlyAgentTokenLimit,
            Plan = request.Plan,
            SubscriptionStatus = request.TrialDays is null
                ? SubscriptionStatus.Active
                : SubscriptionStatus.Trial,
            TrialEndsAtUtc = request.TrialDays is { } days
                ? now.AddDays(days)
                : null
        };

        _tenants.Add(tenant);

        await _tenants.SaveChangesAsync(cancellationToken);

        // Each tenant gets its own editable copy of the template agents its
        // plan covers. A template whose modules are missing would offer the
        // model none of its specialised tools, so it is not installed.
        var enabledModules = tenant.GetEnabledModules();
        var allowedProfiles = tenant.GetAllowedModelProfiles();

        foreach (var template in AgentTemplates.Default)
        {
            if (!template.Modules.All(enabledModules.Contains))
                continue;

            var agent = template.CreateAgent(tenant.Id, now);

            // E.g. Starter only includes the economy profile.
            agent.ModelProfile = template.ChooseModelProfile(allowedProfiles);

            _agents.Add(agent);
        }

        await _agents.SaveChangesAsync(cancellationToken);

        var invitation = await _invitations.IssueAsync(
            tenant.Id,
            request.AdminEmail,
            TenantRoles.Admin,
            cancellationToken);

        if (!invitation.Success)
        {
            // Abort the transaction: no tenant without its first Admin.
            throw new ProvisioningFailedException(
                invitation.ErrorMessage,
                invitation.StatusCode);
        }

        return new ProvisionTenantResult
        {
            Tenant = Map(tenant, activeMemberCount: 0),
            AdminInvitation = invitation.Data!
        };
    }

    public async Task<ServiceResult<PlatformTenantDto>> SetStatusAsync(
        int tenantId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        if (!IsPlatformAdmin())
        {
            return Forbidden<PlatformTenantDto>();
        }

        var tenant = await _tenants.FirstOrDefaultTrackedAsync(
            x => x.Id == tenantId,
            cancellationToken);

        if (tenant is null)
        {
            return ServiceResult<PlatformTenantDto>.Fail(
                "Tenant was not found.",
                404);
        }

        // Deactivating blocks tenant selection and token refresh for every
        // member (see AuthService); current access tokens run out on expiry.
        tenant.IsActive = isActive;

        await _tenants.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(
            AuditCategory.Tenant,
            isActive ? "TenantActivated" : "TenantDeactivated",
            AuditOutcome.Succeeded,
            actorType: AuditActorType.User,
            resourceType: "Tenant",
            resourceId: tenant.Id.ToString(),
            cancellationToken: cancellationToken,
            tenantId: tenant.Id);

        var memberCount = await _memberships.CountAsync(
            x => x.TenantId == tenant.Id && x.IsActive,
            cancellationToken);

        return ServiceResult<PlatformTenantDto>.Ok(Map(tenant, memberCount));
    }

    public async Task<ServiceResult<TenantInvitationLinkDto>> InviteAdminAsync(
        int tenantId,
        string email,
        CancellationToken cancellationToken = default)
    {
        if (!IsPlatformAdmin())
        {
            return Forbidden<TenantInvitationLinkDto>();
        }

        return await _invitations.IssueAsync(
            tenantId,
            email,
            TenantRoles.Admin,
            cancellationToken);
    }

    public async Task<ServiceResult<PlatformTenantDto>> UpdateSubscriptionAsync(
        int tenantId,
        UpdateTenantSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsPlatformAdmin())
        {
            return Forbidden<PlatformTenantDto>();
        }

        if (SubscriptionPlans.Find(request.Plan) is not { } plan)
        {
            return ServiceResult<PlatformTenantDto>.Fail(
                UnknownPlanMessage(request.Plan),
                400);
        }

        if (!Enum.IsDefined(request.SubscriptionStatus))
        {
            return ServiceResult<PlatformTenantDto>.Fail(
                "Subscription status is not valid.",
                400);
        }

        var addOns = (request.AddOnModules ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var unknown = addOns.Where(x => PlatformModules.Find(x) is null).ToList();

        if (unknown.Count > 0)
        {
            return ServiceResult<PlatformTenantDto>.Fail(
                $"Unknown module(s): {string.Join(", ", unknown)}.",
                400);
        }

        var profileAddOns = (request.AddOnModelProfiles ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var unknownProfiles = profileAddOns.Where(x => !_modelProfiles.HasProfile(x)).ToList();

        if (unknownProfiles.Count > 0)
        {
            return ServiceResult<PlatformTenantDto>.Fail(
                $"Unknown model profile(s): {string.Join(", ", unknownProfiles)}.",
                400);
        }

        var tenant = await _tenants.FirstOrDefaultTrackedAsync(
            x => x.Id == tenantId,
            cancellationToken);

        if (tenant is null)
        {
            return ServiceResult<PlatformTenantDto>.Fail(
                "Tenant was not found.",
                404);
        }

        var previous = $"{tenant.Plan}/{tenant.SubscriptionStatus}";

        tenant.Plan = plan.Id;
        tenant.SubscriptionStatus = request.SubscriptionStatus;
        tenant.TrialEndsAtUtc = request.SubscriptionStatus == SubscriptionStatus.Trial
            ? request.TrialEndsAtUtc
            : null;
        // Modules the plan already includes are not stored as add-ons, so a
        // later downgrade removes them as expected.
        tenant.AddOnModules = addOns.Where(x => !plan.Modules.Contains(x)).ToList();
        tenant.AddOnModelProfiles = profileAddOns.Where(x => !plan.ModelProfiles.Contains(x)).ToList();
        tenant.MonthlyAgentTokenLimit = request.MonthlyAgentTokenLimit;

        await _tenants.SaveChangesAsync(cancellationToken);

        var addOnText = tenant.AddOnModules.Count == 0
            ? "none"
            : string.Join(", ", tenant.AddOnModules);

        await _auditService.RecordAsync(
            AuditCategory.Tenant,
            "TenantSubscriptionChanged",
            AuditOutcome.Succeeded,
            actorType: AuditActorType.User,
            resourceType: "Tenant",
            resourceId: tenant.Id.ToString(),
            reason:
                $"{previous} -> {tenant.Plan}/{tenant.SubscriptionStatus}; add-ons: {addOnText}; " +
                $"profiles: {string.Join(", ", tenant.GetAllowedModelProfiles())}.",
            cancellationToken: cancellationToken,
            tenantId: tenant.Id);

        var memberCount = await _memberships.CountAsync(
            x => x.TenantId == tenant.Id && x.IsActive,
            cancellationToken);

        return ServiceResult<PlatformTenantDto>.Ok(Map(tenant, memberCount));
    }

    public ServiceResult<List<string>> ListModelProfiles()
    {
        if (!IsPlatformAdmin())
        {
            return Forbidden<List<string>>();
        }

        return ServiceResult<List<string>>.Ok(
            _modelProfiles.GetProfiles().Select(x => x.Name).ToList());
    }

    private static string UnknownPlanMessage(string? plan) =>
        $"Unknown plan '{plan}'. Use one of: {string.Join(", ", SubscriptionPlans.All.Select(x => x.Id))}.";

    private bool IsPlatformAdmin() =>
        _authorization.HasPermission(Permissions.PlatformTenantsManage);

    private static ServiceResult<T> Forbidden<T>() =>
        ServiceResult<T>.Fail(
            "Only platform administrators can manage tenants.",
            403);

    private static PlatformTenantDto Map(Tenant tenant, int activeMemberCount)
    {
        return new PlatformTenantDto
        {
            TenantId = tenant.Id,
            Name = tenant.Name,
            Slug = tenant.Slug,
            IsActive = tenant.IsActive,
            CreatedAtUtc = tenant.CreatedAtUtc,
            ActiveMemberCount = activeMemberCount,
            MonthlyAgentTokenLimit = tenant.MonthlyAgentTokenLimit,
            Plan = tenant.Plan,
            SubscriptionStatus = tenant.SubscriptionStatus,
            TrialEndsAtUtc = tenant.TrialEndsAtUtc,
            AddOnModules = tenant.AddOnModules.ToList(),
            EnabledModules = tenant.GetEnabledModules().Order().ToList(),
            AddOnModelProfiles = tenant.AddOnModelProfiles.ToList(),
            AllowedModelProfiles = tenant.GetAllowedModelProfiles().ToList(),
            HasAccess = tenant.HasAccess(DateTime.UtcNow)
        };
    }

    private sealed class ProvisioningFailedException : Exception
    {
        public ProvisioningFailedException(string message, int? statusCode)
            : base(message)
        {
            StatusCode = statusCode;
        }

        public int? StatusCode { get; }
    }
}
