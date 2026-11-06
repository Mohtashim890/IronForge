namespace IronForge.Shared.Models.Gym
{
    /// <summary>The currencies a gym can bill in (one per gym).</summary>
    public static class GymCurrencies
    {
        public static readonly IReadOnlyList<string> All =
        [
            "PKR", "MYR", "SGD", "USD", "EUR", "GBP", "INR", "AED", "SAR",
            "CAD", "AUD", "NZD", "ZAR", "CHF", "SEK", "NOK", "DKK", "JPY"
        ];

        public static bool IsKnown(string? currency) =>
            currency is not null && All.Contains(currency.Trim().ToUpperInvariant());
    }

    /// <summary>
    /// Time zones offered in forms (IANA ids). Any valid IANA id is accepted;
    /// these are the common ones for IronForge's markets.
    /// </summary>
    public static class GymTimeZones
    {
        public static readonly IReadOnlyList<string> Common =
        [
            "Asia/Karachi", "Asia/Kuala_Lumpur", "Asia/Singapore", "Asia/Dubai", "Asia/Riyadh",
            "Asia/Kolkata", "Europe/London", "Europe/Berlin", "America/New_York", "America/Chicago",
            "America/Los_Angeles", "Australia/Sydney", "UTC"
        ];
    }
}
