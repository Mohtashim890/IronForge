using System.ComponentModel.DataAnnotations;
using IronForge.Application.Security;
using Microsoft.Extensions.Options;

namespace IronForge.Application.Gym.Billing.Payments;

/// <summary>Ids of the online payment providers; stored on gym accounts, invoices and payments.</summary>
public static class PaymentProviderIds
{
    /// <summary>HitPay (Malaysia: FPX, DuitNow, Touch 'n Go, cards).</summary>
    public const string HitPay = "hitpay";

    /// <summary>Safepay (Pakistan: cards and wallets).</summary>
    public const string Safepay = "safepay";

    /// <summary>A test page where "paying" moves no money (local and demo deployments only).</summary>
    public const string Simulated = "simulated";
}

public sealed class PaymentOptions
{
    public const string SectionName = "Payments";

    /// <summary>
    /// Offer the simulated checkout to gyms without a payment provider.
    /// Members can then "pay" without money moving, so it is for local
    /// development and demo deployments only, never with real gyms.
    /// </summary>
    public bool AllowSimulated { get; set; }

    /// <summary>
    /// Encrypts gyms' payment provider keys in the database (32+ characters).
    /// Empty uses JwtSettings:Key. Changing it means every gym enters its keys again.
    /// </summary>
    [Required]
    [MinLength(32)]
    public string EncryptionKey { get; set; } = "";
}

/// <summary>A gym's provider credentials, decrypted for one call. Never logged or returned to clients.</summary>
public sealed record PaymentAccountCredentials(
    string ApiKey,
    string Secret,
    string? WebhookSecret,
    bool Live);

public sealed class CheckoutRequest
{
    public required int TenantId { get; init; }

    public required int InvoiceId { get; init; }

    public required string InvoiceNumber { get; init; }

    public required string Description { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    /// <summary>Ties the provider's payment to the invoice; checked again when the payment is confirmed.</summary>
    public required string Reference { get; init; }

    public string? CustomerEmail { get; init; }

    public string? CustomerName { get; init; }

    /// <summary>Where the payer's browser goes afterwards; the API then asks the provider for the outcome.</summary>
    public required string ReturnUrl { get; init; }

    public required string CancelUrl { get; init; }
}

/// <summary>A started checkout: the provider's id for it and the page where the member pays.</summary>
public sealed record CheckoutSession(string SessionId, string Url);

public enum CheckoutState
{
    Pending,
    Paid,
    Failed
}

/// <summary>What the provider's own API says about a checkout: the only basis for marking an invoice paid.</summary>
public sealed record CheckoutStatus(
    CheckoutState State,
    decimal? Amount,
    string? Currency,
    string? Reference,
    string? PaymentReference);

/// <summary>
/// A webhook whose signature checked out. <see cref="SessionId"/> is the
/// checkout it is about, or null for events IronForge does not act on.
/// </summary>
public sealed record WebhookNotice(string? SessionId);

/// <summary>What a provider asks of a gym, shown on the payment settings page.</summary>
public sealed class PaymentProviderDescriptor
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Region { get; init; }

    /// <summary>Invoice currencies the provider charges in ("*" for any).</summary>
    public required IReadOnlyList<string> Currencies { get; init; }

    /// <summary>A gym can pick it (the simulated provider is chosen by the platform, not a gym).</summary>
    public bool Selectable { get; init; } = true;

    public required string ApiKeyLabel { get; init; }

    public required string SecretLabel { get; init; }

    /// <summary>Null when the webhook is signed with <see cref="SecretLabel"/>'s value.</summary>
    public string? WebhookSecretLabel { get; init; }

    public string? SandboxDashboardUrl { get; init; }

    public string? LiveDashboardUrl { get; init; }

    /// <summary>Where in the provider's dashboard the gym registers IronForge's webhook URL.</summary>
    public string? WebhookSetup { get; init; }
}

/// <summary>
/// One online payment provider (IronForge.Payments). The gym is the
/// merchant: every call uses the gym's own credentials, so money goes from
/// the member to the gym's provider account and never through IronForge.
/// </summary>
public interface IPaymentProvider
{
    PaymentProviderDescriptor Descriptor { get; }

    /// <exception cref="PaymentProviderException">The provider refused or could not be reached.</exception>
    Task<CheckoutSession> CreateCheckoutAsync(
        PaymentAccountCredentials account,
        CheckoutRequest request,
        CancellationToken cancellationToken = default);

    /// <exception cref="PaymentProviderException">The provider refused or could not be reached.</exception>
    Task<CheckoutStatus> GetStatusAsync(
        PaymentAccountCredentials account,
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>Null when the credentials work; otherwise what is wrong, for the gym admin.</summary>
    Task<string?> VerifyCredentialsAsync(
        PaymentAccountCredentials account,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Null when the webhook's signature is missing or wrong. A valid one
    /// only prompts a status check with the provider; its content is not trusted.
    /// </summary>
    WebhookNotice? ReadWebhook(
        PaymentAccountCredentials account,
        IReadOnlyDictionary<string, string> headers,
        string body);
}

/// <summary>A provider error with a message safe to show to staff or members.</summary>
public sealed class PaymentProviderException : Exception
{
    public PaymentProviderException(string message, Exception? inner = null, int? statusCode = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }

    /// <summary>The provider's HTTP status; null when it could not be reached.</summary>
    public int? StatusCode { get; }
}

/// <summary>The registered providers (DI: every <see cref="IPaymentProvider"/>).</summary>
public interface IPaymentProviderCatalog
{
    IPaymentProvider? Find(string? providerId);

    /// <summary>The providers gyms can choose, in display order.</summary>
    IReadOnlyList<IPaymentProvider> Selectable { get; }
}

public sealed class PaymentProviderCatalog : IPaymentProviderCatalog
{
    private readonly IReadOnlyList<IPaymentProvider> _providers;

    public PaymentProviderCatalog(IEnumerable<IPaymentProvider> providers)
    {
        _providers = providers.ToList();
    }

    public IPaymentProvider? Find(string? providerId) =>
        _providers.FirstOrDefault(x => string.Equals(x.Descriptor.Id, providerId, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<IPaymentProvider> Selectable =>
        _providers.Where(x => x.Descriptor.Selectable).ToList();
}

/// <summary>Encrypts gyms' provider keys at rest (AES-256-GCM, key from Payments:EncryptionKey).</summary>
public interface IPaymentCredentialProtector
{
    string Protect(string value);

    string Unprotect(string protectedValue);
}

public sealed class PaymentCredentialProtector : IPaymentCredentialProtector
{
    private readonly AesGcmProtector _protector;

    public PaymentCredentialProtector(IOptions<PaymentOptions> options)
    {
        _protector = new AesGcmProtector(options.Value.EncryptionKey, "IronForge.PaymentCredentials.v1");
    }

    public string Protect(string value) => _protector.ProtectString(value);

    public string Unprotect(string protectedValue) => _protector.UnprotectString(protectedValue);
}
