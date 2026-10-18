using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Commons;
using IronForge.Application.Entities;
using IronForge.Application.Gym.Billing;
using IronForge.Application.Gym.Common;
using IronForge.Application.Gym.Members;
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
    Task<ServiceResult<MemberPassDto>> GetMyPassAsync(
        CancellationToken cancellationToken = default);

    /// <summary>A lost or shared phone: the old pass stops working on every device.</summary>
    Task<ServiceResult<MemberPassDto>> ResetMyPassAsync(
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
    private readonly TimeProvider _clock;

    public MemberPassService(
        IGymAccessService access,
        IRepository<MemberProfile> members,
        IRepository<MemberPhoto> photos,
        ICheckInKeys keys,
        IBillingEngine billing,
        IAuditService audit,
        TimeProvider clock)
    {
        _access = access;
        _members = members;
        _photos = photos;
        _keys = keys;
        _billing = billing;
        _audit = audit;
        _clock = clock;
    }

    public async Task<ServiceResult<MemberPassDto>> GetMyPassAsync(
        CancellationToken cancellationToken = default)
    {
        var current = await _access.GetCurrentMemberAsync(Permissions.GymSelfRead, cancellationToken: cancellationToken);

        if (!current.Success)
        {
            return GymResults.From<MemberPassDto, MemberProfile>(current);
        }

        return await MapAsync(current.Data!, cancellationToken);
    }

    public async Task<ServiceResult<MemberPassDto>> ResetMyPassAsync(
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
            PhotoDataUrl = member.PhotoUpdatedAtUtc is null
                ? null
                : await MemberPhotoService.LoadDataUrlAsync(_photos, member.Id, cancellationToken),
            IssuedAtUtc = _clock.GetUtcNow().UtcDateTime
        });
    }

    private Task RecordAsync(MemberProfile member, string reason, CancellationToken cancellationToken) =>
        _audit.RecordAsync(
            AuditCategory.Membership,
            "MemberPassReset",
            AuditOutcome.Succeeded,
            resourceType: "MemberProfile",
            resourceId: member.Id.ToString(),
            reason: $"{member.FullName}: {reason}",
            cancellationToken: cancellationToken);
}
