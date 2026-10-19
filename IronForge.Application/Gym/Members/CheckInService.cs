using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Commons;
using IronForge.Application.Entities;
using IronForge.Application.Gym.Billing;
using IronForge.Application.Gym.CheckIns;
using IronForge.Application.Gym.Common;
using IronForge.Application.Persistence;
using IronForge.Shared.Authorization;
using IronForge.Shared.CheckIns;
using IronForge.Shared.Models.Gym;

namespace IronForge.Application.Gym.Members;

public interface ICheckInService
{
    /// <summary>Front desk: staff picked the member by name.</summary>
    Task<ServiceResult<CheckInResultDto>> CheckInAsync(
        int memberId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A scanned member pass, from staff at the front desk or from a paired
    /// check-in device (the caller decides which). A refused entry is a
    /// successful call with <see cref="CheckInOutcome.Denied"/>.
    /// </summary>
    Task<ServiceResult<CheckInResultDto>> ScanPassAsync(
        string code,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The member scanned the code a check-in device shows (gym QR). The
    /// member is the caller; the code proves they are at that device.
    /// </summary>
    Task<ServiceResult<CheckInResultDto>> CheckInWithGymQrAsync(
        string code,
        string? phoneId,
        CancellationToken cancellationToken = default);

    /// <summary>The entrance code the calling check-in device shows now.</summary>
    Task<ServiceResult<GymQrDto>> GetGymQrAsync(
        CancellationToken cancellationToken = default);

    Task<ServiceResult<List<CheckInDto>>> ListRecentAsync(
        int take,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<List<CheckInDenialDto>>> ListDenialsAsync(
        int take,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<AttendanceSummaryDto>> GetAttendanceAsync(
        int days,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<List<CheckInDto>>> ListMineAsync(
        CancellationToken cancellationToken = default);
}

public sealed class CheckInService : ICheckInService
{
    /// <summary>How a check-in was attempted, and by whom, for the visit or refusal row.</summary>
    private sealed record Attempt(CheckInMethod Method, int? DeviceId, int? RecordedByUserId);

    /// <summary>A second check-in within this window is treated as a duplicate scan.</summary>
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Pass steps accepted around the server's: one minute back and one step
    /// ahead, for phones whose clock is slightly off.
    /// </summary>
    private const int PastSteps = 2;
    private const int FutureSteps = 1;

    private readonly IGymAccessService _access;
    private readonly IRepository<CheckIn> _checkIns;
    private readonly IRepository<CheckInDenial> _denials;
    private readonly IRepository<MemberProfile> _members;
    private readonly IRepository<Invoice> _invoices;
    private readonly IRepository<MemberPhoto> _photos;
    private readonly IRepository<CheckInDevice> _devices;
    private readonly ICheckInKeys _keys;
    private readonly IBillingEngine _billing;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;

    public CheckInService(
        IGymAccessService access,
        IRepository<CheckIn> checkIns,
        IRepository<CheckInDenial> denials,
        IRepository<MemberProfile> members,
        IRepository<Invoice> invoices,
        IRepository<MemberPhoto> photos,
        IRepository<CheckInDevice> devices,
        ICheckInKeys keys,
        IBillingEngine billing,
        IAuditService audit,
        TimeProvider clock)
    {
        _access = access;
        _checkIns = checkIns;
        _denials = denials;
        _members = members;
        _invoices = invoices;
        _photos = photos;
        _devices = devices;
        _keys = keys;
        _billing = billing;
        _audit = audit;
        _clock = clock;
    }

    public async Task<ServiceResult<CheckInResultDto>> CheckInAsync(
        int memberId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymCheckInsManage, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<CheckInResultDto>(authorization);
        }

        var member = await _members.FirstOrDefaultTrackedAsync(
            x => x.Id == memberId && x.AnonymizedAtUtc == null,
            cancellationToken);

        if (member is null)
        {
            return GymResults.NotFound<CheckInResultDto>("Member");
        }

        return ServiceResult<CheckInResultDto>.Ok(
            await AdmitAsync(member, new Attempt(CheckInMethod.FrontDesk, null, _access.UserId), cancellationToken));
    }

    public async Task<ServiceResult<CheckInResultDto>> ScanPassAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        var isDevice = _access.DeviceId.HasValue;
        var method = isDevice ? CheckInMethod.Kiosk : CheckInMethod.PassScan;

        var authorization = await _access.AuthorizeAsync(
            isDevice ? Permissions.GymCheckInsDevice : Permissions.GymCheckInsManage,
            cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<CheckInResultDto>(authorization);
        }

        var gym = await _billing.GetGymAsync(_access.TenantId!.Value, cancellationToken);

        if (gym is null || !gym.Settings.MemberPassCheckInEnabled)
        {
            return GymResults.Conflict<CheckInResultDto>(
                "Check-in with the member pass is turned off for this gym. An admin can turn it on in Gym settings.");
        }

        var attempt = new Attempt(method, _access.DeviceId, _access.UserId);

        if (!MemberPassCode.TryParse(code, out var pass))
        {
            return Ok(await DenyAsync(null, attempt, CheckInDenialReason.InvalidPass,
                "This is not an IronForge member pass.", cancellationToken));
        }

        // Never confirm whether a member id exists in another gym.
        var member = pass!.TenantId == _access.TenantId
            ? await _members.FirstOrDefaultTrackedAsync(
                x => x.Id == pass.MemberId && x.AnonymizedAtUtc == null,
                cancellationToken)
            : null;

        if (member is null)
        {
            return Ok(await DenyAsync(null, attempt, CheckInDenialReason.UnknownMember,
                "This pass is not for a member of this gym.", cancellationToken));
        }

        if (pass.KeyVersion != member.PassKeyVersion)
        {
            return Ok(await DenyAsync(member, attempt, CheckInDenialReason.RevokedPass,
                "This pass was reset. Open the IronForge app to show the current pass.", cancellationToken));
        }

        if (!pass.HasValidSignature(_keys.MemberPassSecret(member.TenantId, member.Id, member.PassKeyVersion)))
        {
            await AuditSuspiciousAsync(member, "PassForgeryRejected", "the pass signature does not match", cancellationToken);

            return Ok(await DenyAsync(member, attempt, CheckInDenialReason.InvalidPass,
                "This pass is not valid.", cancellationToken));
        }

        var currentStep = MemberPassCode.StepAt(_clock.GetUtcNow());

        if (pass.Step < currentStep - PastSteps || pass.Step > currentStep + FutureSteps)
        {
            return Ok(await DenyAsync(member, attempt, CheckInDenialReason.ExpiredPass,
                "This code has expired. Show the live pass in the IronForge app, not a screenshot.", cancellationToken));
        }

        if (member.LastPassStep is { } last && pass.Step <= last)
        {
            await AuditSuspiciousAsync(member, "PassReuseRejected", "a code that was already scanned was shown again", cancellationToken);

            return Ok(await DenyAsync(member, attempt, CheckInDenialReason.ReusedPass,
                "This code was already used. Copies of a pass don't work: show the live pass.", cancellationToken));
        }

        member.LastPassStep = pass.Step;

        return Ok(await AdmitAsync(member, attempt, cancellationToken));
    }

    public async Task<ServiceResult<CheckInResultDto>> CheckInWithGymQrAsync(
        string code,
        string? phoneId,
        CancellationToken cancellationToken = default)
    {
        var current = await _access.GetCurrentMemberAsync(
            Permissions.GymSelfCheckIn,
            tracked: true,
            cancellationToken: cancellationToken);

        if (!current.Success)
        {
            return GymResults.From<CheckInResultDto, MemberProfile>(current);
        }

        var member = current.Data!;
        var gym = await _billing.GetGymAsync(member.TenantId, cancellationToken);

        if (gym is null || !gym.Settings.GymQrCheckInEnabled)
        {
            return GymResults.Conflict<CheckInResultDto>(
                "Your gym does not use the entrance code. Show your pass, or check in at the front desk.");
        }

        // Only from the member's own phone: someone signed in to the member's
        // account on another phone cannot check in for them.
        var phone = await PhoneBinding.CheckAsync(member, phoneId, _clock, _members, cancellationToken);

        if (phone is not null)
        {
            return GymResults.From<CheckInResultDto, bool>(phone);
        }

        if (!GymQrCode.TryParse(code, out var qr) || qr!.TenantId != member.TenantId)
        {
            return Ok(await DenyAsync(member, new Attempt(CheckInMethod.GymQr, null, null),
                CheckInDenialReason.InvalidGymQr,
                "That is not this gym's entrance code. Scan the code on the check-in screen.", cancellationToken));
        }

        // The device must still be paired: removing it also retires its codes.
        var device = (await _devices.QueryAsync(
                q => q.Where(x => x.Id == qr.DeviceId && x.RevokedAtUtc == null && x.KeyHash != null),
                cancellationToken))
            .FirstOrDefault();

        var attempt = new Attempt(CheckInMethod.GymQr, device?.Id, null);

        if (device is null || !qr.HasValidSignature(_keys.GymQrSecret(member.TenantId, device.Id)))
        {
            await AuditSuspiciousAsync(member, "GymQrRejected", "an entrance code that this gym did not issue", cancellationToken);

            return Ok(await DenyAsync(member, attempt, CheckInDenialReason.InvalidGymQr,
                "That is not this gym's entrance code. Scan the code on the check-in screen.", cancellationToken));
        }

        // Current step or the one before: at most 30 seconds old.
        var currentStep = GymQrCode.StepAt(_clock.GetUtcNow());

        if (qr.Step < currentStep - 1 || qr.Step > currentStep)
        {
            return Ok(await DenyAsync(member, attempt, CheckInDenialReason.ExpiredGymQr,
                "That code has expired. Scan the code on the screen again; photos of it don't work.", cancellationToken));
        }

        return Ok(await AdmitAsync(member, attempt, cancellationToken));
    }

    public async Task<ServiceResult<GymQrDto>> GetGymQrAsync(
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymCheckInsDevice, cancellationToken);

        if (!authorization.Allowed || _access.DeviceId is not { } deviceId)
        {
            return GymResults.Forbidden<GymQrDto>(authorization);
        }

        var tenantId = _access.TenantId!.Value;
        var gym = await _billing.GetGymAsync(tenantId, cancellationToken);

        if (gym is null || !gym.Settings.GymQrCheckInEnabled)
        {
            return GymResults.Conflict<GymQrDto>("The entrance code is turned off for this gym.");
        }

        var now = _clock.GetUtcNow();

        return ServiceResult<GymQrDto>.Ok(new GymQrDto
        {
            Code = GymQrCode.Create(_keys.GymQrSecret(tenantId, deviceId), tenantId, deviceId, GymQrCode.StepAt(now)),
            StepSeconds = GymQrCode.StepSeconds,
            ExpiresAtUtc = GymQrCode.StepEndsAt(now)
        });
    }

    public async Task<ServiceResult<List<CheckInDto>>> ListRecentAsync(
        int take,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymMembersRead, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<List<CheckInDto>>(authorization);
        }

        take = Math.Clamp(take, 1, 200);

        var items = await _checkIns.QueryAsync(
            q => q
                .OrderByDescending(x => x.CheckedInAtUtc)
                .Take(take)
                .Select(x => new CheckInDto
                {
                    Id = x.Id,
                    MemberId = x.MemberProfileId,
                    MemberName = x.MemberProfile!.FullName,
                    CheckedInAtUtc = x.CheckedInAtUtc,
                    SelfCheckIn = x.Method == CheckInMethod.Kiosk || x.Method == CheckInMethod.GymQr || x.Method == CheckInMethod.Terminal,
                    Warning = x.Warning,
                    Method = x.Method,
                    DeviceName = x.CheckInDevice != null ? x.CheckInDevice.Name : null
                }),
            cancellationToken);

        return ServiceResult<List<CheckInDto>>.Ok(items);
    }

    public async Task<ServiceResult<List<CheckInDenialDto>>> ListDenialsAsync(
        int take,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymMembersRead, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<List<CheckInDenialDto>>(authorization);
        }

        take = Math.Clamp(take, 1, 200);

        var items = await _denials.QueryAsync(
            q => q
                .OrderByDescending(x => x.AttemptedAtUtc)
                .Take(take)
                .Select(x => new CheckInDenialDto
                {
                    Id = x.Id,
                    MemberId = x.MemberProfileId,
                    MemberName = x.MemberProfile != null ? x.MemberProfile.FullName : null,
                    Method = x.Method,
                    DeviceName = x.CheckInDevice != null ? x.CheckInDevice.Name : null,
                    Reason = x.Reason,
                    Message = x.Message,
                    AttemptedAtUtc = x.AttemptedAtUtc
                }),
            cancellationToken);

        return ServiceResult<List<CheckInDenialDto>>.Ok(items);
    }

    public async Task<ServiceResult<AttendanceSummaryDto>> GetAttendanceAsync(
        int days,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymMembersRead, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<AttendanceSummaryDto>(authorization);
        }

        days = Math.Clamp(days, 1, 90);

        var gym = await _billing.GetGymAsync(_access.TenantId!.Value, cancellationToken);
        var timeZone = gym?.Settings.TimeZoneId;
        var zone = GymCalendar.ResolveTimeZone(timeZone);
        var today = GymCalendar.Today(_clock, timeZone);
        var from = today.AddDays(-(days - 1));
        var fromUtc = GymCalendar.StartOfDayUtc(from, timeZone);

        var checkIns = await _checkIns.QueryAsync(
            q => q
                .Where(x => x.CheckedInAtUtc >= fromUtc)
                .Select(x => new { x.MemberProfileId, x.CheckedInAtUtc }),
            cancellationToken);

        var local = checkIns
            .Select(x => new
            {
                x.MemberProfileId,
                Time = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(x.CheckedInAtUtc, DateTimeKind.Utc), zone)
            })
            .ToList();

        var byDay = local
            .GroupBy(x => DateOnly.FromDateTime(x.Time))
            .ToDictionary(x => x.Key, x => x.Count());

        var attended = local.Select(x => x.MemberProfileId).ToHashSet();

        var inactive = await _members.QueryAsync(
            q => q
                .Where(x => x.Status == MemberStatus.Active && x.AnonymizedAtUtc == null)
                .Where(x => !x.CheckIns.Any(c => c.CheckedInAtUtc >= fromUtc))
                .OrderBy(x => x.FullName)
                .Take(25)
                .Select(MemberService.ProjectSummary),
            cancellationToken);

        return ServiceResult<AttendanceSummaryDto>.Ok(new AttendanceSummaryDto
        {
            From = from,
            To = today,
            TotalCheckIns = local.Count,
            UniqueMembers = attended.Count,
            BusiestDayOfWeek = local
                .GroupBy(x => x.Time.DayOfWeek)
                .OrderByDescending(x => x.Count())
                .Select(x => x.Key.ToString())
                .FirstOrDefault(),
            BusiestHour = local
                .GroupBy(x => x.Time.Hour)
                .OrderByDescending(x => x.Count())
                .Select(x => (int?)x.Key)
                .FirstOrDefault(),
            ByDay = Enumerable.Range(0, days)
                .Select(i => from.AddDays(i))
                .Select(day => new DailyCountDto { Day = day, Count = byDay.GetValueOrDefault(day) })
                .ToList(),
            InactiveMembers = inactive
        });
    }

    public async Task<ServiceResult<List<CheckInDto>>> ListMineAsync(
        CancellationToken cancellationToken = default)
    {
        var member = await _access.GetCurrentMemberAsync(Permissions.GymSelfRead, cancellationToken: cancellationToken);

        if (!member.Success)
        {
            return GymResults.From<List<CheckInDto>, MemberProfile>(member);
        }

        var memberId = member.Data!.Id;
        var name = member.Data.FullName;

        var items = await _checkIns.QueryAsync(
            q => q
                .Where(x => x.MemberProfileId == memberId)
                .OrderByDescending(x => x.CheckedInAtUtc)
                .Take(60)
                .Select(x => new CheckInDto
                {
                    Id = x.Id,
                    MemberId = x.MemberProfileId,
                    MemberName = name,
                    CheckedInAtUtc = x.CheckedInAtUtc,
                    SelfCheckIn = x.Method == CheckInMethod.Kiosk || x.Method == CheckInMethod.GymQr || x.Method == CheckInMethod.Terminal,
                    Warning = x.Warning,
                    Method = x.Method,
                    DeviceName = x.CheckInDevice != null ? x.CheckInDevice.Name : null
                }),
            cancellationToken);

        return ServiceResult<List<CheckInDto>>.Ok(items);
    }

    /// <summary>
    /// Applies <see cref="CheckInPolicy"/> to an identified member and
    /// records the visit or the refusal. Saves once, together with any change
    /// already made to the tracked member (the accepted pass step).
    /// </summary>
    private async Task<CheckInResultDto> AdmitAsync(
        MemberProfile member,
        Attempt attempt,
        CancellationToken cancellationToken)
    {
        var gym = await _billing.GetGymAsync(member.TenantId, cancellationToken);
        var timeZone = gym?.Settings.TimeZoneId;
        var today = GymCalendar.Today(_clock, timeZone);
        var memberId = member.Id;

        var overdue = (await _invoices.QueryAsync(
                q => q
                    .Where(x => x.MemberProfileId == memberId &&
                                (x.Status == InvoiceStatus.Overdue ||
                                 (x.Status == InvoiceStatus.Open && x.DueDate < today)))
                    .OrderBy(x => x.DueDate)
                    .Take(1)
                    .Select(x => new OverdueInvoice(x.Number, x.Amount, x.Currency, x.DueDate)),
                cancellationToken))
            .FirstOrDefault();

        var decision = CheckInPolicy.Evaluate(member, overdue, today, gym?.Settings.GracePeriodDays ?? 14);

        if (!decision.Allowed)
        {
            return await DenyAsync(member, attempt, decision.Reason!.Value, decision.Message, cancellationToken);
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var recent = now - DuplicateWindow;

        var existing = (await _checkIns.QueryAsync(
                q => q
                    .Where(x => x.MemberProfileId == memberId && x.CheckedInAtUtc >= recent)
                    .OrderByDescending(x => x.CheckedInAtUtc)
                    .Take(1),
                cancellationToken))
            .FirstOrDefault();

        var result = await DescribeAsync(member, cancellationToken);
        result.Warning = decision.Warning;

        if (existing is not null)
        {
            await _checkIns.SaveChangesAsync(cancellationToken);

            var at = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(existing.CheckedInAtUtc, DateTimeKind.Utc),
                GymCalendar.ResolveTimeZone(timeZone));

            result.Outcome = CheckInOutcome.AlreadyCheckedIn;
            result.Message = $"{member.FullName} already checked in at {at:HH:mm}.";
            result.CheckIn = Map(existing, member.FullName);

            return result;
        }

        var checkIn = new CheckIn
        {
            TenantId = member.TenantId,
            MemberProfileId = member.Id,
            CheckedInAtUtc = now,
            RecordedByUserId = attempt.RecordedByUserId,
            Method = attempt.Method,
            CheckInDeviceId = attempt.DeviceId
        };

        _checkIns.Add(checkIn);
        await _checkIns.SaveChangesAsync(cancellationToken);

        result.Outcome = CheckInOutcome.Allowed;
        result.Message = decision.Message;
        result.CheckIn = Map(checkIn, member.FullName);

        return result;
    }

    private async Task<CheckInResultDto> DenyAsync(
        MemberProfile? member,
        Attempt attempt,
        CheckInDenialReason reason,
        string message,
        CancellationToken cancellationToken)
    {
        _denials.Add(new CheckInDenial
        {
            TenantId = _access.TenantId!.Value,
            MemberProfileId = member?.Id,
            Method = attempt.Method,
            CheckInDeviceId = attempt.DeviceId,
            RecordedByUserId = attempt.RecordedByUserId,
            Reason = reason,
            Message = message.Length > 300 ? message[..300] : message,
            AttemptedAtUtc = _clock.GetUtcNow().UtcDateTime
        });

        await _denials.SaveChangesAsync(cancellationToken);

        // An unattended device must not show who owns a copied or forged pass.
        var identify = member is not null && (!_access.DeviceId.HasValue || !IsPassProblem(reason));

        var result = identify
            ? await DescribeAsync(member!, cancellationToken)
            : new CheckInResultDto();

        result.Outcome = CheckInOutcome.Denied;
        result.DenialReason = reason;
        result.Message = identify || member is null ? message : PassProblemMessage(reason);

        if (_access.DeviceId.HasValue)
        {
            // The kiosk screen faces the public: no photo of a refused member.
            result.PhotoDataUrl = null;
        }

        return result;
    }

    /// <summary>The member's name, status, plan and photo for the screen.</summary>
    private async Task<CheckInResultDto> DescribeAsync(
        MemberProfile member,
        CancellationToken cancellationToken)
    {
        var planId = member.MembershipPlanId;
        var planName = planId is null
            ? null
            : (await _members.QueryAsync(
                    q => q
                        .Where(x => x.Id == member.Id && x.MembershipPlan != null)
                        .Select(x => x.MembershipPlan!.Name),
                    cancellationToken))
                .FirstOrDefault();

        return new CheckInResultDto
        {
            MemberId = member.Id,
            MemberName = member.FullName,
            Status = member.Status,
            PlanName = planName,
            PhotoDataUrl = member.PhotoUpdatedAtUtc is null
                ? null
                : await MemberPhotoService.LoadDataUrlAsync(_photos, member.Id, cancellationToken)
        };
    }

    private static bool IsPassProblem(CheckInDenialReason reason) =>
        reason is CheckInDenialReason.InvalidPass
            or CheckInDenialReason.ExpiredPass
            or CheckInDenialReason.ReusedPass
            or CheckInDenialReason.RevokedPass;

    private static string PassProblemMessage(CheckInDenialReason reason) => reason switch
    {
        CheckInDenialReason.ExpiredPass => "This code has expired. Show the live pass in the IronForge app.",
        CheckInDenialReason.ReusedPass => "This code was already used. Show the live pass in the IronForge app.",
        CheckInDenialReason.RevokedPass => "This pass was reset. Open the IronForge app to show the current pass.",
        _ => "This pass is not valid. Please see the front desk."
    };

    private static CheckInDto Map(CheckIn checkIn, string memberName) => new()
    {
        Id = checkIn.Id,
        MemberId = checkIn.MemberProfileId,
        MemberName = memberName,
        CheckedInAtUtc = checkIn.CheckedInAtUtc,
        SelfCheckIn = checkIn.Method == CheckInMethod.Kiosk || checkIn.Method == CheckInMethod.GymQr,
        Method = checkIn.Method
    };

    private Task AuditSuspiciousAsync(
        MemberProfile member,
        string action,
        string reason,
        CancellationToken cancellationToken) =>
        _audit.RecordAsync(
            AuditCategory.Membership,
            action,
            AuditOutcome.Denied,
            resourceType: "MemberProfile",
            resourceId: member.Id.ToString(),
            reason: $"{member.FullName}: {reason}",
            cancellationToken: cancellationToken);

    private static ServiceResult<CheckInResultDto> Ok(CheckInResultDto result) =>
        ServiceResult<CheckInResultDto>.Ok(result);
}
