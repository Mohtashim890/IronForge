using System.ComponentModel.DataAnnotations;

namespace IronForge.Shared.Models.Gym
{
    /// <summary>A payment provider a gym can connect, with what it asks for.</summary>
    public sealed class PaymentProviderInfoDto
    {
        public string Id { get; set; } = "";

        public string Name { get; set; } = "";

        public string Region { get; set; } = "";

        public List<string> Currencies { get; set; } = [];

        /// <summary>It charges in the gym's currency.</summary>
        public bool SupportsGymCurrency { get; set; }

        public string ApiKeyLabel { get; set; } = "";

        public string SecretLabel { get; set; } = "";

        /// <summary>Null when the provider signs webhooks with the secret above.</summary>
        public string? WebhookSecretLabel { get; set; }

        public string? SandboxDashboardUrl { get; set; }

        public string? LiveDashboardUrl { get; set; }

        public string? WebhookSetup { get; set; }
    }

    /// <summary>The gym's online payment setup. Keys are never returned, only a hint.</summary>
    public sealed class PaymentAccountDto
    {
        public bool Configured { get; set; }

        public string? Provider { get; set; }

        public string? ProviderName { get; set; }

        /// <summary>Live keys take real money; otherwise the provider's sandbox.</summary>
        public bool IsLive { get; set; }

        /// <summary>Last characters of the stored API key.</summary>
        public string? ApiKeyHint { get; set; }

        public bool HasWebhookSecret { get; set; }

        public DateTime? VerifiedAtUtc { get; set; }

        public DateTime? UpdatedAtUtc { get; set; }

        /// <summary>The URL to register in the provider's dashboard for payment notifications.</summary>
        public string? WebhookUrl { get; set; }

        public string Currency { get; set; } = "";

        /// <summary>Members can pay in the app now.</summary>
        public bool OnlinePaymentsAvailable { get; set; }

        /// <summary>The platform runs the test checkout for gyms without a provider (no money moves).</summary>
        public bool SimulatedFallback { get; set; }

        public List<PaymentProviderInfoDto> Providers { get; set; } = [];
    }

    /// <summary>
    /// Connects or updates the gym's provider. Blank keys keep the stored
    /// ones (same provider), so staff never need to see them again.
    /// </summary>
    public sealed class SavePaymentAccountRequest
    {
        [Required]
        [MaxLength(30)]
        public string Provider { get; set; } = "";

        public bool IsLive { get; set; }

        [MaxLength(500)]
        public string? ApiKey { get; set; }

        [MaxLength(500)]
        public string? Secret { get; set; }

        [MaxLength(500)]
        public string? WebhookSecret { get; set; }
    }

    /// <summary>Outcome of asking the provider about a member's checkout.</summary>
    public sealed class CheckoutCheckDto
    {
        public bool Paid { get; set; }

        public InvoiceStatus Status { get; set; }

        public string Message { get; set; } = "";
    }
}
