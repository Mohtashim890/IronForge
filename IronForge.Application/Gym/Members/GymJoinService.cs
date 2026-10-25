using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Auth.Services;
using IronForge.Application.Commons;
using IronForge.Application.Entities;
using IronForge.Application.Gym.Common;
using IronForge.Application.Gym.Notifications;
using IronForge.Application.Persistence;
using IronForge.Application.Tenants.Invitations;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Gym;
using Microsoft.AspNetCore.Identity;

namespace IronForge.Application.Gym.Members;

/// <summary>
/// Gym data needed before a gym is selected (joining, accepting an invitation)
/// or across gyms (background jobs). Bypasses the tenant query filter and
/// scopes every query to an explicit tenant instead.
/// </summary>
public interface IGymDirectory
{
    Task<List<MembershipPlan>> ListActivePlansAsync(int tenantId, CancellationToken cancellationToken = default);

    Task<MemberProfile?> FindMemberByEmailAsync(
        int tenantId,
        string normalizedEmail,
        CancellationToken cancellationToken = default);

    /// <summary>Tracked: a declined join request is reopened.</summary>
    Task<MemberProfile?> FindMemberByEmailTrackedAsync(
        int tenantId,
        string normalizedEmail,
        CancellationToken cancellationToken = default);

    /// <summary>The account's gym memberships that staff have not approved (pending or declined).</summary>
    Task<List<UnapprovedMembership>> ListUnapprovedMembershipsAsync(
        int userId,
        CancellationToken cancellationToken = default);

    /// <summary>Tracked: the profile is linked to the new account.</summary>
    Task<MemberProfile?> FindUnlinkedMemberByEmailTrackedAsync(
        int tenantId,
        string normalizedEmail,
        CancellationToken cancellationToken = default);

    /// <summary>Gyms whose subscription currently grants access; billing runs for these only.</summary>
    Task<List<int>> ListServedTenantIdsAsync(DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Active Admins of a gym, to notify them of join requests.</summary>
    Task<List<NotificationRecipient>> ListAdminRecipientsAsync(int tenantId, CancellationToken cancellationToken = default);
}

/// <summary>A join request of an account that staff have not approved yet, or declined.</summary>
public sealed record UnapprovedMembership(int TenantId, string GymName, bool Declined);

public interface IGymJoinService
{
    Task<ServiceResult<GymJoinPreviewDto>> PreviewAsync(
        string joinCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Anonymous sign-up with the gym's join code: creates the account (or
    /// links an existing one after checking its password) and a Pending
    /// membership that staff approve.
    /// </summary>
    Task<ServiceResult<GymJoinResultDto>> JoinAsync(
        GymJoinRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class GymJoinService : IGymJoinService
{
    private const string InvalidCodeMessage = "This join code is not valid.";

    private readonly IRepository<GymSettings> _settings;
    private readonly IRepository<Tenant> _tenants;
    private readonly IRepository<User> _users;
    private readonly IRepository<TenantMembership> _memberships;
    private readonly IRepository<MemberProfile> _members;
    private readonly IGymDirectory _directory;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly INotificationService _notifications;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;
    private readonly ITenantInvitationService _invitations;
    private readonly ISignInGuard _signInGuard;

    public GymJoinService(
        IRepository<GymSettings> settings,
        IRepository<Tenant> tenants,
        IRepository<User> users,
        IRepository<TenantMembership> memberships,
        IRepository<MemberProfile> members,
        IGymDirectory directory,
        IPasswordHasher<User> passwordHasher,
        INotificationService notifications,
        IUnitOfWork unitOfWork,
        IAuditService audit,
        TimeProvider clock,
        ITenantInvitationService invitations,
        ISignInGuard signInGuard)
    {
        _signInGuard = signInGuard;
        _invitations = invitations;
        _settings = settings;
        _tenants = tenants;
        _users = users;
        _memberships = memberships;
        _members = members;
        _directory = directory;
        _passwordHasher = passwordHasher;
        _notifications = notifications;
        _unitOfWork = unitOfWork;
        _audit = audit;
        _clock = clock;
    }

    /// <summary>
    /// Joining with an email that has an account needs its username and
    /// password. Wrong passwords count towards the account's lockout; a right
    /// one does not clear it (that needs a full sign-in, with two-factor).
    /// </summary>
    private async Task<ServiceResult<GymJoinResultDto>?> CheckExistingAccountAsync(
        User existingUser,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        var check =
            existingUser.IsActive &&
            string.Equals(existingUser.Username, username, StringComparison.OrdinalIgnoreCase)
                ? await _signInGuard.CheckPasswordAsync(existingUser.Id, password, resetOnSuccess: false, cancellationToken)
                : PasswordCheck.Invalid;

        return check switch
        {
            PasswordCheck.Valid => null,
            PasswordCheck.LockedOut => ServiceResult<GymJoinResultDto>.Fail(SignInGuard.GenericLockedOutMessage, 429),
            _ => ServiceResult<GymJoinResultDto>.Fail(
                "An account with this email already exists. Enter its username and password.",
                401)
        };
    }

    public async Task<ServiceResult<GymJoinPreviewDto>> PreviewAsync(
        string joinCode,
        CancellationToken cancellationToken = default)
    {
        var (gym, tenant) = await ResolveAsync(joinCode, cancellationToken);

        if (gym is null || tenant is null)
        {
            return ServiceResult<GymJoinPreviewDto>.Fail(InvalidCodeMessage, 404);
        }

        var plans = await _directory.ListActivePlansAsync(tenant.Id, cancellationToken);

        return ServiceResult<GymJoinPreviewDto>.Ok(new GymJoinPreviewDto
        {
            GymName = tenant.Name,
            Address = gym.Address,
            Currency = gym.Currency,
            Plans = plans
                .OrderBy(x => x.Price)
                .Select(x => MembershipPlanService.Map(x, gym.Currency, 0))
                .ToList()
        });
    }

    public async Task<ServiceResult<GymJoinResultDto>> JoinAsync(
        GymJoinRequest request,
        CancellationToken cancellationToken = default)
    {
        var (gym, tenant) = await ResolveAsync(request.JoinCode, cancellationToken);

        if (gym is null || tenant is null)
        {
            return ServiceResult<GymJoinResultDto>.Fail(InvalidCodeMessage, 404);
        }

        var email = GymCalendar.NormalizeEmail(request.Email);
        var username = request.Username.Trim();

        var existingProfile = await _directory.FindMemberByEmailAsync(tenant.Id, email, cancellationToken);

        if (existingProfile is { UserId: null, AnonymizedAtUtc: null } && existingProfile.IsApproved)
        {
            // Staff added this person, but they have no sign-in yet.
            return await ConnectStaffAddedMemberAsync(tenant, request, email, username, cancellationToken);
        }

        if (existingProfile is not null && !existingProfile.IsDeclinedRequest)
        {
            return GymResults.Conflict<GymJoinResultDto>(
                existingProfile.IsApproved
                    ? "You are already a member of this gym. Sign in instead, or use \"Forgot password\" if you don't remember it."
                    : "Your request to join this gym is already waiting for approval.");
        }

        MembershipPlan? plan = null;

        if (request.MembershipPlanId is { } planId)
        {
            plan = (await _directory.ListActivePlansAsync(tenant.Id, cancellationToken))
                .FirstOrDefault(x => x.Id == planId);

            if (plan is null)
            {
                return GymResults.Invalid<GymJoinResultDto>("Choose one of the gym's plans.");
            }
        }

        var existingUser = await _users.FirstOrDefaultAsync(
            x => x.Email.ToLower() == email,
            cancellationToken);

        if (existingUser is not null)
        {
            // The email has an account: the person must prove they own it.
            var failure = await CheckExistingAccountAsync(existingUser, username, request.Password, cancellationToken);

            if (failure is not null)
            {
                return failure;
            }
        }
        else if (await _users.AnyAsync(x => x.Username.ToLower() == username.ToLower(), cancellationToken))
        {
            return GymResults.Conflict<GymJoinResultDto>("This username is already taken.");
        }

        if (existingUser is not null &&
            await _memberships.AnyAsync(
                x => x.TenantId == tenant.Id && x.UserId == existingUser.Id && x.IsActive,
                cancellationToken))
        {
            return GymResults.Conflict<GymJoinResultDto>(
                "Your account already has access to this gym. Sign in instead.");
        }

        var now = _clock.GetUtcNow().UtcDateTime;

        var (user, member) = await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var account = existingUser;

            if (account is null)
            {
                account = new User
                {
                    Username = username,
                    Email = email,
                    Role = PlatformRoles.User,
                    IsActive = true,
                    CreatedAtUtc = now
                };

                account.PasswordHash = _passwordHasher.HashPassword(account, request.Password);

                _users.Add(account);
                await _users.SaveChangesAsync(cancellationToken);
            }

            var membership = await _memberships.FirstOrDefaultTrackedAsync(
                x => x.TenantId == tenant.Id && x.UserId == account.Id,
                cancellationToken);

            // Gym access starts when staff approve the request (MemberService.ApproveAsync).
            if (membership is null)
            {
                _memberships.Add(new TenantMembership
                {
                    TenantId = tenant.Id,
                    UserId = account.Id,
                    Role = TenantRoles.Member,
                    IsActive = false,
                    CreatedAtUtc = now
                });
            }
            else
            {
                membership.Role = TenantRoles.Member;
                membership.IsActive = false;
            }

            // A declined request can be made again: the same profile is reopened.
            var profile = existingProfile is null
                ? null
                : await _directory.FindMemberByEmailTrackedAsync(tenant.Id, email, cancellationToken);

            if (profile is null)
            {
                profile = new MemberProfile { TenantId = tenant.Id, Email = email };
                _members.Add(profile);
            }

            profile.UserId = account.Id;
            profile.FullName = request.FullName.Trim();
            profile.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
            profile.MembershipPlanId = plan?.Id;
            profile.Status = MemberStatus.Pending;
            profile.JoinedAtUtc = now;
            profile.CancelledAtUtc = null;

            var admins = await _directory.ListAdminRecipientsAsync(tenant.Id, cancellationToken);

            _notifications.Queue(
                tenant.Id,
                admins,
                new NotificationMessage
                {
                    Type = NotificationType.Membership,
                    Title = "New join request",
                    Body =
                        $"{profile.FullName} ({email}) asked to join" +
                        (plan is null ? "." : $" on the {plan.Name} plan.") +
                        "\n\nReview it on the Join requests page.",
                    Link = "/join-requests",
                    SendEmail = true,
                    GymName = tenant.Name
                });

            await _members.SaveChangesAsync(cancellationToken);

            return (account, profile);
        }, cancellationToken);

        await _audit.RecordAsync(
            AuditCategory.Membership,
            "MemberJoinRequested",
            AuditOutcome.Succeeded,
            AuditActorType.User,
            "MemberProfile",
            member.Id.ToString(),
            reason: existingUser is null
                ? $"New account '{user.Username}' asked to join."
                : $"Existing account '{user.Username}' asked to join.",
            cancellationToken: cancellationToken,
            tenantId: tenant.Id);

        return ServiceResult<GymJoinResultDto>.Ok(new GymJoinResultDto
        {
            GymName = tenant.Name,
            Username = user.Username,
            AccountCreated = existingUser is null,
            Status = member.Status
        });
    }

    /// <summary>
    /// The gym already added this person (Members page) without an app
    /// account. Knowing the email is not proof of owning it, so the profile is
    /// never handed over on the email alone:
    /// - an existing account with this email: its username and password prove
    ///   ownership, and the account is linked to the membership;
    /// - no account: an invitation is emailed; accepting it creates the
    ///   account and links the membership (TenantInvitationService).
    /// </summary>
    private async Task<ServiceResult<GymJoinResultDto>> ConnectStaffAddedMemberAsync(
        Tenant tenant,
        GymJoinRequest request,
        string email,
        string username,
        CancellationToken cancellationToken)
    {
        var profile = await _directory.FindMemberByEmailTrackedAsync(tenant.Id, email, cancellationToken);

        if (profile is null || profile.UserId is not null)
        {
            return GymResults.Conflict<GymJoinResultDto>("You are already a member of this gym. Sign in instead.");
        }

        var existingUser = await _users.FirstOrDefaultAsync(
            x => x.Email.ToLower() == email,
            cancellationToken);

        if (existingUser is null)
        {
            var invitation = await _invitations.IssueAsync(tenant.Id, email, TenantRoles.Member, cancellationToken);

            if (!invitation.Success)
            {
                return ServiceResult<GymJoinResultDto>.Fail(invitation.ErrorMessage, invitation.StatusCode);
            }

            await _audit.RecordAsync(
                AuditCategory.Membership,
                "MemberAppInvitationRequested",
                AuditOutcome.Succeeded,
                AuditActorType.User,
                "MemberProfile",
                profile.Id.ToString(),
                reason: "Joined with the gym code after staff had added them; an invitation was emailed.",
                cancellationToken: cancellationToken,
                tenantId: tenant.Id);

            return ServiceResult<GymJoinResultDto>.Ok(new GymJoinResultDto
            {
                GymName = tenant.Name,
                Status = profile.Status,
                InvitationEmailed = true
            });
        }

        var failure = await CheckExistingAccountAsync(existingUser, username, request.Password, cancellationToken);

        if (failure is not null)
        {
            return failure;
        }

        profile.UserId = existingUser.Id;

        var membership = await _memberships.FirstOrDefaultTrackedAsync(
            x => x.TenantId == tenant.Id && x.UserId == existingUser.Id,
            cancellationToken);

        if (membership is null)
        {
            _memberships.Add(new TenantMembership
            {
                TenantId = tenant.Id,
                UserId = existingUser.Id,
                Role = TenantRoles.Member,
                IsActive = true,
                CreatedAtUtc = _clock.GetUtcNow().UtcDateTime
            });
        }
        else if (membership.Role == TenantRoles.Member)
        {
            // Staff approved this person when they added them.
            membership.IsActive = true;
        }

        await _members.SaveChangesAsync(cancellationToken);

        await _audit.RecordAsync(
            AuditCategory.Membership,
            "MemberAccountLinked",
            AuditOutcome.Succeeded,
            AuditActorType.User,
            "MemberProfile",
            profile.Id.ToString(),
            reason: $"Account '{existingUser.Username}' linked with the gym code.",
            cancellationToken: cancellationToken,
            tenantId: tenant.Id);

        return ServiceResult<GymJoinResultDto>.Ok(new GymJoinResultDto
        {
            GymName = tenant.Name,
            Username = existingUser.Username,
            Status = profile.Status,
            AccessGranted = true
        });
    }

    private async Task<(GymSettings? Gym, Tenant? Tenant)> ResolveAsync(
        string joinCode,
        CancellationToken cancellationToken)
    {
        var code = GymCalendar.NormalizeJoinCode(joinCode);

        if (code.Length is < 4 or > 20)
        {
            return (null, null);
        }

        var gym = await _settings.FirstOrDefaultAsync(x => x.JoinCode == code, cancellationToken);

        if (gym is null)
        {
            return (null, null);
        }

        var tenant = await _tenants.FirstOrDefaultAsync(x => x.Id == gym.TenantId, cancellationToken);

        // A suspended or canceled gym does not take new members.
        return tenant is not null && tenant.HasAccess(_clock.GetUtcNow().UtcDateTime)
            ? (gym, tenant)
            : (null, null);
    }
}
