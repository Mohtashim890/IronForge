namespace IronForge.Infrastructure.Payments;

/// <summary>
/// Stripe settings. Leave SecretKey empty to use the simulated gateway
/// (local development and demos): no real money moves.
/// </summary>
public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    public string? SecretKey { get; set; }

    public string? WebhookSecret { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SecretKey);
}
