using System.Globalization;
using IronForge.Application.Configurations;
using IronForge.Application.Gym.Billing.Payments;
using Microsoft.Extensions.Options;

namespace IronForge.Payments.Simulated;

/// <summary>
/// The test checkout for local development and demo deployments
/// (Payments:AllowSimulated): a page served by the API with a "Pay" button
/// that confirms the invoice without money moving. Gyms cannot choose it;
/// it stands in for gyms that have connected no provider.
/// </summary>
public sealed class SimulatedPaymentProvider : IPaymentProvider
{
    private readonly AppUrlOptions _urls;

    public SimulatedPaymentProvider(IOptions<AppUrlOptions> urls) => _urls = urls.Value;

    public PaymentProviderDescriptor Descriptor { get; } = new()
    {
        Id = PaymentProviderIds.Simulated,
        Name = "Test checkout",
        Region = "Demo: no money moves",
        Currencies = ["*"],
        Selectable = false,
        ApiKeyLabel = "",
        SecretLabel = ""
    };

    public Task<CheckoutSession> CreateCheckoutAsync(
        PaymentAccountCredentials account,
        CheckoutRequest request,
        CancellationToken cancellationToken = default)
    {
        // A random session stored on the invoice: the page cannot be opened for another invoice.
        var sessionId = $"sim_{Guid.NewGuid():N}";

        var url =
            $"{_urls.ApiBaseUrl.TrimEnd('/')}/payments/simulated" +
            $"?tenant={request.TenantId.ToString(CultureInfo.InvariantCulture)}" +
            $"&invoice={request.InvoiceId.ToString(CultureInfo.InvariantCulture)}&session={sessionId}";

        return Task.FromResult(new CheckoutSession(sessionId, url));
    }

    /// <summary>The test page confirms the payment itself; there is nothing to ask.</summary>
    public Task<CheckoutStatus> GetStatusAsync(
        PaymentAccountCredentials account,
        string sessionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new CheckoutStatus(CheckoutState.Pending, null, null, null, null));

    public Task<string?> VerifyCredentialsAsync(
        PaymentAccountCredentials account,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public WebhookNotice? ReadWebhook(
        PaymentAccountCredentials account,
        IReadOnlyDictionary<string, string> headers,
        string body) =>
        null;
}
