namespace IronForge.Infrastructure.Payments;

/// <summary>
/// Stripe settings. Leave SecretKey empty for no card payments (or the
/// simulated checkout where <see cref="PaymentOptions.AllowSimulated"/> is on).
/// </summary>
public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    public string? SecretKey { get; set; }

    public string? WebhookSecret { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SecretKey);

    /// <summary>Test keys may charge the platform account (demos); live keys need the gym's connected account.</summary>
    public bool IsTestMode => SecretKey?.StartsWith("sk_test_", StringComparison.Ordinal) == true;
}

public sealed class PaymentOptions
{
    public const string SectionName = "Payments";

    /// <summary>
    /// Offer the simulated checkout when no payment provider is configured.
    /// Members can then "pay" without money moving, so it is for local
    /// development and demo deployments only, never with real gyms.
    /// </summary>
    public bool AllowSimulated { get; set; }
}
