using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Auth.Services;
using IronForge.Application.Commons;
using IronForge.Application.Configurations;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.Persistence;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Tenants;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace IronForge.Application.Tenants.Invitations;

public interface ITenantInvitationService
{
    /// <summary>Open invitations of the current tenant. Requires tenant.members.read.</summary>
    Task<ServiceResult<List<TenantInvitationDto>>> ListForCurrentTenantAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Invites someone to the current tenant. Requires tenant.members.manage.</summary>
    Task<ServiceResult<TenantInvitationLinkDto>> CreateForCurrentTenantAsync(
        CreateTenantInvitationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Requires tenant.members.manage.</summary>
    Task<ServiceResult<bool>> RevokeForCurrentTenantAsync(
        Guid invitationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Issues an invitation to any tenant without permission checks.
    /// The caller is responsible for authorization.
    /// </summary>
    Task<ServiceResult<TenantInvitationLinkDto>> IssueAsync(
        int tenantId,
        string email,
        string role,
        CancellationToken cancellationToken = default);

    /// <summary>Anonymous: what the token invites to.</summary>
    Task<ServiceResult<InvitationPreviewDto>> PreviewAsync(
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Anonymous: accepts the invitation. Creates the account when no account
    /// has the invited email; otherwise the invitee proves ownership of that
    /// account with its username and password.
    /// </summary>
    Task<ServiceResult<AcceptInvitationResultDto>> AcceptAsync(
        AcceptInvitationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class TenantInvitationService : ITenantInvitationService
{
    private const string InvalidInvitationMessage =
        "This invitation link is invalid.";

    private readonly ITenantInvitationStore _invitations;
    private readonly IRepository<Tenant> _tenants;
    private readonly IRepository<User> _users;
    private readonly IRepository<TenantMembership> _memberships;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserAuthorizationService _authorization;
    private readonly IExecutionContextAccessor _executionContext;
    private readonly IAuditService _auditService;
    private readonly InvitationOptions _options;

    public TenantInvitationService(
        ITenantInvitationStore invitations,
        IRepository<Tenant> tenants,
        IRepository<User> users,
        IRepository<TenantMembership> memberships,
        IPasswordHasher<User> passwordHasher,
        IUnitOfWork unitOfWork,
        IUserAuthorizationService authorization,
        IExecutionContextAccessor executionContext,
        IAuditService auditService,
        IOptions<InvitationOptions> options)
    {
        _invitations = invitations;
        _tenants = tenants;
        _users = users;
        _memberships = memberships;
        _passwordHasher = passwordHasher;
        _unitOfWork = unitOfWork;
        _authorization = authorization;
        _executionContext = executionContext;
        _auditService = auditService;
        _options = options.Value;
    }

    public async Task<ServiceResult<List<TenantInvitationDto>>> ListForCurrentTenantAsync(
        CancellationToken cancellationToken = default)
    {
        var tenantId = _executionContext.Current?.TenantId;

        if (tenantId is null)
        {
            return ServiceResult<List<TenantInvitationDto>>.Fail(
                "A tenant must be selected.",
                403);
        }

        if (!_authorization.HasPermission(Permissions.MembersRead))
        {
            return ServiceResult<List<TenantInvitationDto>>.Fail(
                "You are not authorized to view invitations.",
                403);
        }

        var now = DateTime.UtcNow;

        var invitations = await _invitations.ListOpenAsync(
            tenantId.Value,
            cancellationToken: cancellationToken);

        return ServiceResult<List<TenantInvitationDto>>.Ok(
            invitations
                .Select(x => Map(x, now))
                .ToList());
    }

    public async Task<ServiceResult<TenantInvitationLinkDto>> CreateForCurrentTenantAsync(
        CreateTenantInvitationRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _executionContext.Current?.TenantId;

        if (tenantId is null)
        {
            return ServiceResult<TenantInvitationLinkDto>.Fail(
                "A tenant must be selected.",
                403);
        }

        if (!_authorization.HasPermission(Permissions.MembersManage))
        {
            return ServiceResult<TenantInvitationLinkDto>.Fail(
                "You are not authorized to invite members.",
                403);
        }

        return await IssueAsync(
            tenantId.Value,
            request.Email,
            request.Role,
            cancellationToken);
    }

    public async Task<ServiceResult<bool>> RevokeForCurrentTenantAsync(
        Guid invitationId,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _executionContext.Current?.TenantId;

        if (tenantId is null)
        {
            return ServiceResult<bool>.Fail(
                "A tenant must be selected.",
                403);
        }

        if (!_authorization.HasPermission(Permissions.MembersManage))
        {
            return ServiceResult<bool>.Fail(
                "You are not authorized to revoke invitations.",
                403);
        }

        var invitation = await _invitations.FindAsync(
            tenantId.Value,
            invitationId,
            cancellationToken);

        if (invitation is null ||
            invitation.AcceptedAtUtc != null ||
            invitation.RevokedAtUtc != null)
        {
            return ServiceResult<bool>.Fail(
                "Invitation was not found.",
                404);
        }

        invitation.RevokedAtUtc = DateTime.UtcNow;

        await _invitations.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(
            AuditCategory.Tenant,
            "InvitationRevoked",
            AuditOutcome.Succeeded,
            resourceType: "TenantInvitation",
            resourceId: invitation.Id.ToString(),
            cancellationToken: cancellationToken);

        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<TenantInvitationLinkDto>> IssueAsync(
        int tenantId,
        string email,
        string role,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return ServiceResult<TenantInvitationLinkDto>.Fail(
                "Email is required.",
                400);
        }

        var canonicalRole = TenantRoles.Normalize(role);

        if (canonicalRole is null)
        {
            return ServiceResult<TenantInvitationLinkDto>.Fail(
                $"Role must be one of: {string.Join(", ", TenantRoles.All)}.",
                400);
        }

        var normalizedEmail = InvitationToken.NormalizeEmail(email);

        var tenantExists = await _tenants.AnyAsync(
            x => x.Id == tenantId,
            cancellationToken);

        if (!tenantExists)
        {
            return ServiceResult<TenantInvitationLinkDto>.Fail(
                "Tenant was not found.",
                404);
        }

        var existingUser = await FindUserByEmailAsync(
            normalizedEmail,
            cancellationToken);

        if (existingUser != null &&
            await _memberships.AnyAsync(
                x =>
                    x.TenantId == tenantId &&
                    x.UserId == existingUser.Id &&
                    x.IsActive,
                cancellationToken))
        {
            return ServiceResult<TenantInvitationLinkDto>.Fail(
                "This person is already an active member of the tenant.",
                409);
        }

        var now = DateTime.UtcNow;

        // A new invitation replaces any open one for the same email, so only
        // the most recent link works.
        var previous = await _invitations.ListOpenAsync(
            tenantId,
            normalizedEmail,
            cancellationToken);

        foreach (var open in previous)
        {
            open.RevokedAtUtc = now;
        }

        var token = InvitationToken.Create();

        var invitation = new TenantInvitation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = normalizedEmail,
            Role = canonicalRole,
            TokenHash = InvitationToken.Hash(token),
            CreatedByUserId = _executionContext.Current?.Actor.UserId,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(_options.LifetimeDays)
        };

        _invitations.Add(invitation);

        await _invitations.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(
            AuditCategory.Tenant,
            "InvitationCreated",
            AuditOutcome.Succeeded,
            resourceType: "TenantInvitation",
            resourceId: invitation.Id.ToString(),
            reason: $"Invited as {canonicalRole}.",
            cancellationToken: cancellationToken,
            tenantId: tenantId);

        return ServiceResult<TenantInvitationLinkDto>.Ok(
            new TenantInvitationLinkDto
            {
                Invitation = Map(invitation, now),
                Token = token,
                AcceptUrl = BuildAcceptUrl(token)
            });
    }

    public async Task<ServiceResult<InvitationPreviewDto>> PreviewAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var (invitation, tenant, error) =
            await ResolveUsableInvitationAsync(token, cancellationToken);

        if (invitation is null || tenant is null)
        {
            return ServiceResult<InvitationPreviewDto>.Fail(error, 404);
        }

        var accountExists =
            await FindUserByEmailAsync(invitation.Email, cancellationToken) != null;

        return ServiceResult<InvitationPreviewDto>.Ok(
            new InvitationPreviewDto
            {
                TenantName = tenant.Name,
                Email = invitation.Email,
                Role = invitation.Role,
                ExpiresAtUtc = invitation.ExpiresAtUtc,
                AccountExists = accountExists
            });
    }

    public async Task<ServiceResult<AcceptInvitationResultDto>> AcceptAsync(
        AcceptInvitationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(
                () => AcceptInTransactionAsync(request, cancellationToken),
                cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return ServiceResult<AcceptInvitationResultDto>.Fail(
                "This invitation has already been used.",
                409);
        }
    }

    private async Task<ServiceResult<AcceptInvitationResultDto>> AcceptInTransactionAsync(
        AcceptInvitationRequest request,
        CancellationToken cancellationToken)
    {
        var (invitation, tenant, error) =
            await ResolveUsableInvitationAsync(request.Token, cancellationToken);

        if (invitation is null || tenant is null)
        {
            return ServiceResult<AcceptInvitationResultDto>.Fail(error, 404);
        }

        var now = DateTime.UtcNow;
        var username = request.Username.Trim();

        var user = await FindUserByEmailAsync(
            invitation.Email,
            cancellationToken,
            tracked: true);

        var accountCreated = false;

        if (user != null)
        {
            // The invitation is bound to an email that already has an account:
            // the invitee must prove they own it.
            var credentialsValid =
                user.IsActive &&
                string.Equals(user.Username, username, StringComparison.OrdinalIgnoreCase) &&
                _passwordHasher.VerifyHashedPassword(
                    user,
                    user.PasswordHash,
                    request.Password) != PasswordVerificationResult.Failed;

            if (!credentialsValid)
            {
                await _auditService.RecordAsync(
                    AuditCategory.Tenant,
                    "InvitationAcceptDenied",
                    AuditOutcome.Denied,
                    actorType: AuditActorType.User,
                    resourceType: "TenantInvitation",
                    resourceId: invitation.Id.ToString(),
                    reason: "Invalid credentials for the invited account.",
                    cancellationToken: cancellationToken,
                    tenantId: invitation.TenantId);

                return ServiceResult<AcceptInvitationResultDto>.Fail(
                    "An account with this email already exists. Enter its username and password.",
                    401);
            }
        }
        else
        {
            if (await _users.AnyAsync(
                    x => x.Username.ToLower() == username.ToLower(),
                    cancellationToken))
            {
                return ServiceResult<AcceptInvitationResultDto>.Fail(
                    "This username is already taken.",
                    409);
            }

            user = new User
            {
                Username = username,
                Email = invitation.Email,
                Role = PlatformRoles.User,
                IsActive = true,
                CreatedAtUtc = now
            };

            user.PasswordHash = _passwordHasher.HashPassword(
                user,
                request.Password);

            _users.Add(user);

            // Saved now to get the user id; rolled back if anything below fails.
            await _users.SaveChangesAsync(cancellationToken);

            accountCreated = true;
        }

        var membership = await _memberships.FirstOrDefaultTrackedAsync(
            x => x.TenantId == invitation.TenantId && x.UserId == user.Id,
            cancellationToken);

        if (membership is null)
        {
            _memberships.Add(new TenantMembership
            {
                TenantId = invitation.TenantId,
                UserId = user.Id,
                Role = invitation.Role,
                IsActive = true,
                CreatedAtUtc = now
            });
        }
        else
        {
            // A former (deactivated) member rejoins with the invited role.
            membership.Role = invitation.Role;
            membership.IsActive = true;
        }

        invitation.AcceptedAtUtc = now;
        invitation.AcceptedByUserId = user.Id;

        await _invitations.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(
            AuditCategory.Tenant,
            "InvitationAccepted",
            AuditOutcome.Succeeded,
            actorType: AuditActorType.User,
            resourceType: "TenantInvitation",
            resourceId: invitation.Id.ToString(),
            reason: accountCreated
                ? $"New account '{user.Username}' joined as {invitation.Role}."
                : $"Existing account '{user.Username}' joined as {invitation.Role}.",
            cancellationToken: cancellationToken,
            tenantId: invitation.TenantId);

        return ServiceResult<AcceptInvitationResultDto>.Ok(
            new AcceptInvitationResultDto
            {
                TenantName = tenant.Name,
                Username = user.Username,
                Role = invitation.Role,
                AccountCreated = accountCreated
            });
    }

    private async Task<(TenantInvitation? Invitation, Tenant? Tenant, string Error)>
        ResolveUsableInvitationAsync(
            string token,
            CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return (null, null, InvalidInvitationMessage);
        }

        var invitation = await _invitations.FindByTokenHashAsync(
            InvitationToken.Hash(token),
            cancellationToken);

        if (invitation is null)
            return (null, null, InvalidInvitationMessage);

        if (invitation.AcceptedAtUtc != null)
            return (null, null, "This invitation has already been used.");

        if (invitation.RevokedAtUtc != null)
            return (null, null, "This invitation was withdrawn. Ask for a new one.");

        if (invitation.ExpiresAtUtc <= DateTime.UtcNow)
            return (null, null, "This invitation has expired. Ask for a new one.");

        var tenant = await _tenants.FirstOrDefaultAsync(
            x => x.Id == invitation.TenantId,
            cancellationToken);

        if (tenant is null || !tenant.HasAccess(DateTime.UtcNow))
            return (null, null, "This workspace is not available.");

        return (invitation, tenant, "");
    }

    private Task<User?> FindUserByEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken,
        bool tracked = false)
    {
        // Emails of older accounts may not be lower-case.
        return tracked
            ? _users.FirstOrDefaultTrackedAsync(
                x => x.Email.ToLower() == normalizedEmail,
                cancellationToken)
            : _users.FirstOrDefaultAsync(
                x => x.Email.ToLower() == normalizedEmail,
                cancellationToken);
    }

    private string BuildAcceptUrl(string token)
    {
        var separator = _options.AcceptUrl.Contains('?') ? "&" : "?";

        return $"{_options.AcceptUrl}{separator}token={Uri.EscapeDataString(token)}";
    }

    private static TenantInvitationDto Map(
        TenantInvitation invitation,
        DateTime nowUtc)
    {
        return new TenantInvitationDto
        {
            InvitationId = invitation.Id,
            Email = invitation.Email,
            Role = invitation.Role,
            CreatedAtUtc = invitation.CreatedAtUtc,
            ExpiresAtUtc = invitation.ExpiresAtUtc,
            IsExpired = invitation.ExpiresAtUtc <= nowUtc
        };
    }
}
