namespace IronForge.Shared.Components.Gym;

/// <summary>
/// The time zone the UI shows instants in: the selected gym's. One per
/// circuit (scoped). On a web host, <see cref="Current"/> is set for every
/// piece of circuit work by a CircuitHandler, so the static
/// <see cref="GymFormat"/> helpers can read it; without it (the MAUI app)
/// times show in the device's own zone.
/// </summary>
public sealed class DisplayTimeZone
{
    private static readonly AsyncLocal<DisplayTimeZone?> CurrentZone = new();

    public static DisplayTimeZone? Current
    {
        get => CurrentZone.Value;
        set => CurrentZone.Value = value;
    }

    public TimeZoneInfo? Zone { get; private set; }

    /// <summary>Unknown or empty ids fall back to the device's zone.</summary>
    public void Set(string? timeZoneId)
    {
        Zone = !string.IsNullOrWhiteSpace(timeZoneId) &&
               TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone)
            ? zone
            : null;
    }

    /// <summary>A UTC instant in the display zone.</summary>
    public static DateTime ToDisplay(DateTime utc)
    {
        utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);

        return Current?.Zone is { } zone
            ? TimeZoneInfo.ConvertTimeFromUtc(utc, zone)
            : utc.ToLocalTime();
    }
}
