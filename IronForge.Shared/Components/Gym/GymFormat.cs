using System.Globalization;
using IronForge.Shared.Models.Gym;

namespace IronForge.Shared.Components.Gym;

/// <summary>Display formatting shared by the gym apps.</summary>
public static class GymFormat
{
    public static string Money(decimal amount, string? currency) =>
        $"{amount.ToString(amount % 1 == 0 ? "N0" : "N2", CultureInfo.InvariantCulture)} {currency}".Trim();

    public static string Date(DateOnly? date) =>
        date?.ToString("d MMM yyyy", CultureInfo.InvariantCulture) ?? "—";

    public static string ShortDate(DateOnly date) =>
        date.ToString("d MMM", CultureInfo.InvariantCulture);

    public static string Date(DateTime? utc) =>
        utc is null ? "—" : utc.Value.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    public static string DateTime(DateTime? utc) =>
        utc is null ? "—" : utc.Value.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture);

    public static string Ago(DateTime utc)
    {
        var span = System.DateTime.UtcNow - System.DateTime.SpecifyKind(utc, DateTimeKind.Utc);

        return span.TotalMinutes switch
        {
            < 1 => "just now",
            < 60 => $"{(int)span.TotalMinutes} min ago",
            < 60 * 24 => $"{(int)span.TotalHours} h ago",
            < 60 * 24 * 30 => $"{(int)span.TotalDays} d ago",
            _ => Date(utc)
        };
    }

    public static string Interval(BillingInterval interval) => interval switch
    {
        BillingInterval.Monthly => "per month",
        BillingInterval.Quarterly => "per quarter",
        BillingInterval.Yearly => "per year",
        _ => ""
    };

    public static string Tone(MemberStatus status) => status switch
    {
        MemberStatus.Active => "success",
        MemberStatus.Pending => "info",
        MemberStatus.Frozen => "accent",
        MemberStatus.Expired => "danger",
        _ => ""
    };

    public static string Tone(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Paid => "success",
        InvoiceStatus.Open => "info",
        InvoiceStatus.Overdue => "danger",
        _ => ""
    };

    public static string Initials(string? name)
    {
        var parts = (name ?? "").Split(' ', '.', '_', '@').Where(x => x.Length > 0).ToArray();

        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[1][..1]).ToUpperInvariant()
        };
    }

    public static string Compact(long value) => value switch
    {
        >= 1_000_000 => (value / 1_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + "M",
        >= 1_000 => (value / 1_000d).ToString("0.#", CultureInfo.InvariantCulture) + "k",
        _ => value.ToString(CultureInfo.InvariantCulture)
    };
}

/// <summary>One bar or point of a chart.</summary>
public sealed record ChartPoint(string Label, double Value, bool Muted = false);
