using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Commons;
using IronForge.Application.Entities;
using IronForge.Application.Gym.Billing;
using IronForge.Application.Gym.Common;
using IronForge.Application.Gym.Members;
using IronForge.Application.Gym.Notifications;
using IronForge.Application.Persistence;
using IronForge.Shared.Authorization;
using IronForge.Shared.CheckIns;
using IronForge.Shared.Models.Gym;

namespace IronForge.Application.Gym.CheckIns;

/// <summary>
/// The member's rotating QR pass. The app downloads the pass secret once and
/// then shows codes offline; see <see cref="MemberPassCode"/>.
/// </summary>
public interface IMemberPassService
{
    /// <summary>
    /// The pass for this phone (<paramref name="phoneId"/>: the app
    /// installation). A pass lives on one phone: on another phone this
    /// answers 423 until the member moves it (<see cref="MoveMyPassAsync"/>).
    /// </summary>
    Task<ServiceResult<MemberPassDto>> GetMyPassAsync(
        string? phoneId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the pass to this phone. The pass is reset, so the other phone's
    /// codes stop working, and the member is told by email: a friend using
    /// the member's account takes the pass away from them, which is noticed.
    /// </summary>
    Task<ServiceResult<MemberPassDto>> MoveMyPassAsync(
        string? phoneId,
        CancellationToken cancellationToken = default);

    /// <summary>A lost or shared phone: the old pass stops working on every device.</summary>
    Task<ServiceResult<MemberPassDto>> ResetMyPassAsync(
        string? phoneId,
        CancellationToken cancellationToken = default);

    /// <summary>Staff reset a member's pass, e.g. after a reported lost phone.</summary>
    Task<ServiceResult<bool>> ResetPassAsync(
        int memberId,
        CancellationToken cancellationToken = default);
}

public sealed class MemberPassService : IMemberPassService
{
    private readonly IGymAccessService _access;
    private readonly IRepository<MemberProfile> _members;
    private readonly IRepository<MemberPhoto> _photos;
    private readonly ICheckInKeys _keys;
    private readonly IBillingEngine _billing;
    private readonly IAuditService _audit;
    private readonly INotificationService _notifications;
    private readonly TimeProvider _clock;

    public MemberPassService(
        IGymAccessService access,
        IRepository<MemberProfile> members,
        IRepository<MemberPhoto> photos,
        ICheckInKeys keys,
        IBillingEngine billing,
        IAuditService audit,
        INotificationService notifications,
        TimeProvider clock)
    {
        _access = access;
        _members = members;
        _photos = photos;
        _notifications = notifications;
        _keys = keys;
        _billing = billing;
        _audit = audit;
        _clock = clock;
    }

    public async Task<ServiceResult<MemberPassDto>> GetMyPassAsync(
        string? phoneId,
        CancellationToken cancellationToken = default)
    {
        var current = await _access.GetCurrentMemberAsync(
            Permissions.GymSelfRead,
            tracked: true,
            cancellationToken: cancellationToken);

        if (!current.Success)
        {
            return GymResults.From<MemberPassDto, MemberProfile>(current);
        }

        var member = current.Data!;
        var phone = await PhoneBinding.CheckAsync(member, phoneId, _clock, _members, cancellationToken);

        if (phone is not null)
        {
            return GymResults.From<MemberPassDto, bool>(phone);
        }

        return await MapAsync(member, cancellationToken);
    }

    public async Task<ServiceResult<MemberPassDto>> MoveMyPassAsync(
        string? phoneId,
        CancellationToken cancellationToken = default)
    {
        var current = await _access.GetCurrentMemberAsync(
            Permissions.GymSelfRead,
            tracked: true,
            cancellationToken: cancellationToken);

        if (!current.Success)
        {
            return GymResults.From<MemberPassDto, MemberProfile>(current);
        }

        var member = current.Data!;

        if (PhoneBinding.Hash(phoneId) is not { } hash)
        {
            return GymResults.Invalid<MemberPassDto>(PhoneBinding.MissingMessage);
        }

        var moved = member.PassPhoneIdHash is not null && member.PassPhoneIdHash != hash;

        member.PassPhoneIdHash = hash;
        member.PassPhoneBoundAtUtc = _clock.GetUtcNow().UtcDateTime;

        if (moved)
        {
            Reset(member);

            var gym = await _billing.GetGymAsync(member.TenantId, cancellationToken);

            _notifications.Queue(
                member.TenantId,
                [BillingEngine.Recipient(member)],
                new NotificationMessage
                {
                    Type = NotificationType.Membership,
                    Title = "Your gym pass moved to another phone",
                    Body =
                        $"Your {gym?.Name ?? "gym"} check-in pass was moved to another phone just now; " +
                        "the pass on your previous phone no longer works.\n\n" +
                        "If this wasn't you, change your password and tell the front desk.",
                    Link = "/checkin",
                    SendEmail = true
                });
        }

        await _members.SaveChangesAsync(cancellationToken);

        if (moved)
        {
            await RecordAsync(member, "moved to another phone", cancellationToken, "PassMovedToAnotherPhone");
        }

        return await MapAsync(member, cancellationToken);
    }

    public async Task<ServiceResult<MemberPassDto>> ResetMyPassAsync(
        string? phoneId,
        CancellationToken cancellationToken = default)
    {
        var current = await _access.GetCurrentMemberAsync(
            Permissions.GymSelfRead,
            tracked: true,
            cancellationToken: cancellationToken);

        if (!current.Success)
        {
            return GymResults.From<MemberPassDto, MemberProfile>(current);
        }

        var member = current.Data!;
        var phone = await PhoneBinding.CheckAsync(member, phoneId, _clock, _members, cancellationToken);

        if (phone is not null)
        {
            return GymResults.From<MemberPassDto, bool>(phone);
        }

        Reset(member);
        await _members.SaveChangesAsync(cancellationToken);
        await RecordAsync(member, "reset by the member", cancellationToken);

        return await MapAsync(member, cancellationToken);
    }

    public async Task<ServiceResult<bool>> ResetPassAsync(
        int memberId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymMembersManage, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<bool>(authorization);
        }

        var member = await _members.FirstOrDefaultTrackedAsync(
            x => x.Id == memberId && x.AnonymizedAtUtc == null,
            cancellationToken);

        if (member is null)
        {
            return GymResults.NotFound<bool>("Member");
        }

        Reset(member);

        // The member sets the pass up again on the phone they use now.
        member.PassPhoneIdHash = null;
        member.PassPhoneBoundAtUtc = null;

        await _members.SaveChangesAsync(cancellationToken);
        await RecordAsync(member, "reset by staff", cancellationToken);

        return ServiceResult<bool>.Ok(true);
    }

    private static void Reset(MemberProfile member)
    {
        member.PassKeyVersion++;
        member.LastPassStep = null;
    }

    private async Task<ServiceResult<MemberPassDto>> MapAsync(MemberProfile member, CancellationToken cancellationToken)
    {
        if (!member.IsApproved)
        {
            return GymResults.Conflict<MemberPassDto>(
                "Your pass is ready once the gym approves your membership.");
        }

        var gym = await _billing.GetGymAsync(member.TenantId, cancellationToken);

        if (gym is null || !gym.Settings.MemberPassCheckInEnabled)
        {
            return GymResults.Conflict<MemberPassDto>(
                "Your gym does not use the member pass. Check in at the front desk or with the entrance code.");
        }

        return ServiceResult<MemberPassDto>.Ok(new MemberPassDto
        {
            TenantId = member.TenantId,
            MemberId = member.Id,
            KeyVersion = member.PassKeyVersion,
            Secret = Convert.ToBase64String(_keys.MemberPassSecret(member.TenantId, member.Id, member.PassKeyVersion)),
            StepSeconds = MemberPassCode.DefaultStepSeconds,
            FullName = member.FullName,
            GymName = gym?.Name ?? "",
            Status = member.Status,
            RequirePhoneUnlock = gym.Settings.RequirePhoneUnlock,
            PhotoDataUrl = member.PhotoUpdatedAtUtc is null
                ? null
                : await MemberPhotoService.LoadDataUrlAsync(_photos, member.Id, cancellationToken),
            IssuedAtUtc = _clock.GetUtcNow().UtcDateTime
        });
    }

    private Task RecordAsync(MemberProfile member, string reason, CancellationToken cancellationToken, string action = "MemberPassReset") =>
        _audit.RecordAsync(
            AuditCategory.Membership,
            action,
            AuditOutcome.Succeeded,
            resourceType: "MemberProfile",
            resourceId: member.Id.ToString(),
            reason: $"{member.FullName}: {reason}",
            cancellationToken: cancellationToken);
}

/// <summary>
/// A member's pass belongs to one phone (app installation). The app sends a
/// random installation id; only its hash is stored. The first phone is
/// bound; another phone gets 423 until the member moves the pass.
/// </summary>
public static class PhoneBinding
{
    public const string Header = "X-Phone-Id";

    public const string MissingMessage = "Update the IronForge app to use the check-in pass.";

    public const string OtherPhoneMessage =
        "Your pass is on another phone. Move it to this phone? The pass on the other phone will stop working.";

    public static string? Hash(string? phoneId)
    {
        var value = phoneId?.Trim();

        return string.IsNullOrEmpty(value) || value.Length > 100
            ? null
            : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("phone|" + value))).ToLowerInvariant();
    }

    /// <summary>Null when this phone may use the pass (binding it if it is the first); otherwise the failure.</summary>
    public static async Task<ServiceResult<bool>?> CheckAsync(
        MemberProfile member,
        string? phoneId,
        TimeProvider clock,
        IRepository<MemberProfile> members,
        CancellationToken cancellationToken)
    {
        if (Hash(phoneId) is not { } hash)
        {
            return ServiceResult<bool>.Fail(MissingMessage, 400);
        }

        if (member.PassPhoneIdHash is null)
        {
            member.PassPhoneIdHash = hash;
            member.PassPhoneBoundAtUtc = clock.GetUtcNow().UtcDateTime;
            await members.SaveChangesAsync(cancellationToken);
            return null;
        }

        return member.PassPhoneIdHash == hash
            ? null
            : ServiceResult<bool>.Fail(OtherPhoneMessage, 423);
    }
}
