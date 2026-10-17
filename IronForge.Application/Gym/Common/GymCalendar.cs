using System.Security.Cryptography;
using IronForge.Shared.Models.Gym;

namespace IronForge.Application.Gym.Common;

/// <summary>
/// Date rules of the gym domain. Dates that people see (billing dates, due
/// dates, "today") are calendar dates in the gym's time zone; instants are
/// stored in UTC.
/// </summary>
public static class GymCalendar
{
    public static int Months(BillingInterval interval) => interval switch
    {
        BillingInterval.Monthly => 1,
        BillingInterval.Quarterly => 3,
        BillingInterval.Yearly => 12,
        _ => throw new ArgumentOutOfRangeException(nameof(interval), interval, null)
    };

    /// <summary>Last day of the period that starts on <paramref name="start"/>.</summary>
    public static DateOnly PeriodEnd(DateOnly start, BillingInterval interval) =>
        start.AddMonths(Months(interval)).AddDays(-1);

    public static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId) &&
            TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone))
        {
            return zone;
        }

        return TimeZoneInfo.Utc;
    }

    public static bool IsValidTimeZone(string? timeZoneId) =>
        !string.IsNullOrWhiteSpace(timeZoneId) &&
        TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _);

    public static DateOnly Today(TimeProvider clock, string? timeZoneId) =>
        ToLocalDate(clock.GetUtcNow().UtcDateTime, timeZoneId);

    public static DateOnly ToLocalDate(DateTime utc, string? timeZoneId) =>
        DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(utc, DateTimeKind.Utc),
                ResolveTimeZone(timeZoneId)));

    /// <summary>The UTC instant at which <paramref name="day"/> starts in the gym.</summary>
    public static DateTime StartOfDayUtc(DateOnly day, string? timeZoneId) =>
        TimeZoneInfo.ConvertTimeToUtc(
            day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified),
            ResolveTimeZone(timeZoneId));

    /// <summary>Monday of the week containing <paramref name="day"/>.</summary>
    public static DateOnly WeekStart(DateOnly day) =>
        day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    public static DateOnly MonthStart(DateOnly day) => new(day.Year, day.Month, 1);

    /// <summary>
    /// Consecutive weeks, ending with the current one, that contain at least
    /// one of <paramref name="days"/>. The current week does not break the
    /// streak while it has no workout yet.
    /// </summary>
    public static int WeeklyStreak(IEnumerable<DateOnly> days, DateOnly today)
    {
        var weeks = days.Select(WeekStart).ToHashSet();
        var week = WeekStart(today);

        if (!weeks.Contains(week))
        {
            week = week.AddDays(-7);
        }

        var streak = 0;

        while (weeks.Contains(week))
        {
            streak++;
            week = week.AddDays(-7);
        }

        return streak;
    }

    private const string JoinCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>A short, unambiguous code (no 0/O, 1/I) that members type in.</summary>
    public static string NewJoinCode(int length = 8)
    {
        Span<char> code = stackalloc char[length];

        for (var i = 0; i < length; i++)
        {
            code[i] = JoinCodeAlphabet[RandomNumberGenerator.GetInt32(JoinCodeAlphabet.Length)];
        }

        return new string(code);
    }

    public static string NormalizeJoinCode(string? code) =>
        (code ?? "").Trim().Replace("-", "").Replace(" ", "").ToUpperInvariant();

    public static string FormatInvoiceNumber(int sequence) => $"INV-{sequence:D6}";

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
