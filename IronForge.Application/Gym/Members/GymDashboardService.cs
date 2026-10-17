using IronForge.Application.Commons;
using IronForge.Application.Entities;
using IronForge.Application.Gym.Billing;
using IronForge.Application.Gym.Common;
using IronForge.Application.Persistence;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Gym;

namespace IronForge.Application.Gym.Members;

public interface IGymDashboardService
{
    Task<ServiceResult<GymDashboardDto>> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Active members whose paid period ends within <paramref name="days"/> days.</summary>
    Task<ServiceResult<List<MemberSummaryDto>>> ListExpiringAsync(
        int days,
        CancellationToken cancellationToken = default);
}

public sealed class GymDashboardService : IGymDashboardService
{
    private readonly IGymAccessService _access;
    private readonly IRepository<MemberProfile> _members;
    private readonly IRepository<Invoice> _invoices;
    private readonly IRepository<Payment> _payments;
    private readonly IRepository<CheckIn> _checkIns;
    private readonly IBillingEngine _billing;
    private readonly TimeProvider _clock;

    public GymDashboardService(
        IGymAccessService access,
        IRepository<MemberProfile> members,
        IRepository<Invoice> invoices,
        IRepository<Payment> payments,
        IRepository<CheckIn> checkIns,
        IBillingEngine billing,
        TimeProvider clock)
    {
        _access = access;
        _members = members;
        _invoices = invoices;
        _payments = payments;
        _checkIns = checkIns;
        _billing = billing;
        _clock = clock;
    }

    public async Task<ServiceResult<GymDashboardDto>> GetAsync(CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymMembersRead, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<GymDashboardDto>(authorization);
        }

        var gym = await _billing.GetGymAsync(_access.TenantId!.Value, cancellationToken);
        var timeZone = gym?.Settings.TimeZoneId;
        var today = GymCalendar.Today(_clock, timeZone);
        var monthStart = GymCalendar.MonthStart(today);
        var monthStartUtc = GymCalendar.StartOfDayUtc(monthStart, timeZone);
        var lastMonthStartUtc = GymCalendar.StartOfDayUtc(monthStart.AddMonths(-1), timeZone);
        var todayStartUtc = GymCalendar.StartOfDayUtc(today, timeZone);
        var weekAgoUtc = GymCalendar.StartOfDayUtc(today.AddDays(-6), timeZone);
        var sixMonthsUtc = GymCalendar.StartOfDayUtc(monthStart.AddMonths(-5), timeZone);
        var fourteenDaysUtc = GymCalendar.StartOfDayUtc(today.AddDays(-13), timeZone);

        var statusCounts = (await _members.QueryAsync(
                q => q
                    .Where(x => x.AnonymizedAtUtc == null)
                    .GroupBy(x => x.Status)
                    .Select(g => new { Status = g.Key, Count = g.Count() }),
                cancellationToken))
            .ToDictionary(x => x.Status, x => x.Count);

        var payments = await _payments.QueryAsync(
            q => q
                .Where(x => x.PaidAtUtc >= sixMonthsUtc)
                .Select(x => new { x.PaidAtUtc, x.Amount }),
            cancellationToken);

        var revenueByMonth = payments
            .GroupBy(x => GymCalendar.MonthStart(GymCalendar.ToLocalDate(x.PaidAtUtc, timeZone)))
            .ToDictionary(x => x.Key, x => x.Sum(p => p.Amount));

        var checkIns = await _checkIns.QueryAsync(
            q => q
                .Where(x => x.CheckedInAtUtc >= fourteenDaysUtc)
                .Select(x => x.CheckedInAtUtc),
            cancellationToken);

        var checkInsByDay = checkIns
            .GroupBy(x => GymCalendar.ToLocalDate(x, timeZone))
            .ToDictionary(x => x.Key, x => x.Count());

        var byPlan = await _members.QueryAsync(
            q => q
                .Where(x => x.Status == MemberStatus.Active && x.MembershipPlan != null)
                .GroupBy(x => x.MembershipPlan!.Name)
                .Select(g => new PlanBreakdownDto { PlanName = g.Key, Members = g.Count() }),
            cancellationToken);

        var expiringBy = today.AddDays(7);

        var dashboard = new GymDashboardDto
        {
            GymName = gym?.Name ?? "",
            Currency = gym?.Settings.Currency ?? "USD",
            ActiveMembers = statusCounts.GetValueOrDefault(MemberStatus.Active),
            PendingMembers = statusCounts.GetValueOrDefault(MemberStatus.Pending),
            FrozenMembers = statusCounts.GetValueOrDefault(MemberStatus.Frozen),
            ExpiredMembers = statusCounts.GetValueOrDefault(MemberStatus.Expired),
            NewMembersThisMonth = await _members.CountAsync(
                x => x.JoinedAtUtc >= monthStartUtc && x.Status != MemberStatus.Pending && x.AnonymizedAtUtc == null,
                cancellationToken),
            RevenueThisMonth = payments.Where(x => x.PaidAtUtc >= monthStartUtc).Sum(x => x.Amount),
            RevenueLastMonth = payments
                .Where(x => x.PaidAtUtc >= lastMonthStartUtc && x.PaidAtUtc < monthStartUtc)
                .Sum(x => x.Amount),
            OverdueAmount = await _invoices.QuerySumAsync(
                q => q.Where(x => x.Status == InvoiceStatus.Overdue).Select(x => x.Amount),
                cancellationToken),
            OverdueInvoices = await _invoices.CountAsync(x => x.Status == InvoiceStatus.Overdue, cancellationToken),
            CheckInsToday = checkIns.Count(x => x >= todayStartUtc),
            CheckInsLast7Days = checkIns.Count(x => x >= weekAgoUtc),
            ExpiringSoon = await _members.CountAsync(
                x => x.Status == MemberStatus.Active &&
                     x.PaidThrough != null &&
                     x.PaidThrough >= today &&
                     x.PaidThrough <= expiringBy,
                cancellationToken),
            RevenueByMonth = Enumerable.Range(0, 6)
                .Select(i => monthStart.AddMonths(i - 5))
                .Select(month => new RevenuePointDto { Month = month, Collected = revenueByMonth.GetValueOrDefault(month) })
                .ToList(),
            CheckInsByDay = Enumerable.Range(0, 14)
                .Select(i => today.AddDays(i - 13))
                .Select(day => new DailyCountDto { Day = day, Count = checkInsByDay.GetValueOrDefault(day) })
                .ToList(),
            MembersByPlan = byPlan.OrderByDescending(x => x.Members).ToList()
        };

        return ServiceResult<GymDashboardDto>.Ok(dashboard);
    }

    public async Task<ServiceResult<List<MemberSummaryDto>>> ListExpiringAsync(
        int days,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymMembersRead, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<List<MemberSummaryDto>>(authorization);
        }

        days = Math.Clamp(days, 1, 60);

        var gym = await _billing.GetGymAsync(_access.TenantId!.Value, cancellationToken);
        var today = GymCalendar.Today(_clock, gym?.Settings.TimeZoneId);
        var until = today.AddDays(days);

        var items = await _members.QueryAsync(
            q => q
                .Where(x => x.Status == MemberStatus.Active &&
                            x.AnonymizedAtUtc == null &&
                            x.PaidThrough != null &&
                            x.PaidThrough >= today &&
                            x.PaidThrough <= until)
                .OrderBy(x => x.PaidThrough)
                .Take(100)
                .Select(MemberService.ProjectSummary),
            cancellationToken);

        return ServiceResult<List<MemberSummaryDto>>.Ok(items);
    }
}
