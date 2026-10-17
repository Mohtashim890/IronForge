using IronForge.Application.Commons;
using IronForge.Application.Entities;
using IronForge.Application.Gym.Billing;
using IronForge.Application.Gym.Common;
using IronForge.Application.Persistence;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Gym;

namespace IronForge.Application.Gym.Members;

public interface ICheckInService
{
    /// <summary>Front-desk check-in. Only active members can check in.</summary>
    Task<ServiceResult<CheckInDto>> CheckInAsync(
        int memberId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<List<CheckInDto>>> ListRecentAsync(
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
    /// <summary>A second check-in within this window is treated as a duplicate scan.</summary>
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(30);

    private readonly IGymAccessService _access;
    private readonly IRepository<CheckIn> _checkIns;
    private readonly IRepository<MemberProfile> _members;
    private readonly IBillingEngine _billing;
    private readonly TimeProvider _clock;

    public CheckInService(
        IGymAccessService access,
        IRepository<CheckIn> checkIns,
        IRepository<MemberProfile> members,
        IBillingEngine billing,
        TimeProvider clock)
    {
        _access = access;
        _checkIns = checkIns;
        _members = members;
        _billing = billing;
        _clock = clock;
    }

    public async Task<ServiceResult<CheckInDto>> CheckInAsync(
        int memberId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymCheckInsManage, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<CheckInDto>(authorization);
        }

        var member = await _members.FirstOrDefaultAsync(
            x => x.Id == memberId && x.AnonymizedAtUtc == null,
            cancellationToken);

        if (member is null)
        {
            return GymResults.NotFound<CheckInDto>("Member");
        }

        if (member.Status != MemberStatus.Active)
        {
            return GymResults.Conflict<CheckInDto>(
                member.Status switch
                {
                    MemberStatus.Expired => $"{member.FullName}'s membership has expired: an invoice is overdue.",
                    MemberStatus.Frozen => $"{member.FullName}'s membership is frozen until {member.FrozenUntil:d MMM}.",
                    MemberStatus.Pending => $"{member.FullName}'s join request is not approved yet.",
                    _ => $"{member.FullName}'s membership is {member.Status}."
                });
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var recent = now - DuplicateWindow;

        var existing = await _checkIns.FirstOrDefaultAsync(
            x => x.MemberProfileId == memberId && x.CheckedInAtUtc >= recent,
            cancellationToken);

        var checkIn = existing ?? new CheckIn
        {
            TenantId = member.TenantId,
            MemberProfileId = member.Id,
            CheckedInAtUtc = now,
            RecordedByUserId = _access.UserId
        };

        if (existing is null)
        {
            _checkIns.Add(checkIn);
            await _checkIns.SaveChangesAsync(cancellationToken);
        }

        return ServiceResult<CheckInDto>.Ok(new CheckInDto
        {
            Id = checkIn.Id,
            MemberId = member.Id,
            MemberName = member.FullName,
            CheckedInAtUtc = checkIn.CheckedInAtUtc,
            SelfCheckIn = checkIn.RecordedByUserId is null
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
                    SelfCheckIn = x.RecordedByUserId == null
                }),
            cancellationToken);

        return ServiceResult<List<CheckInDto>>.Ok(items);
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
                    SelfCheckIn = x.RecordedByUserId == null
                }),
            cancellationToken);

        return ServiceResult<List<CheckInDto>>.Ok(items);
    }
}
