using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using IronForge.Application.Gym.Billing.Payments;

namespace IronForge.Payments.HitPay;

/// <summary>
/// HitPay (Malaysia, Singapore): hosted payment-request checkout with FPX,
/// DuitNow, Touch 'n Go and cards.
/// - Create: POST /v1/payment-requests (form-encoded, X-BUSINESS-API-KEY).
/// - Status: GET /v1/payment-requests/{id}; "completed" means paid.
/// - Webhooks: registered in the gym's dashboard (Developers → Webhook
///   Endpoints); JSON body signed with HMAC-SHA256 of the raw body keyed with
///   the salt, in the Hitpay-Signature header.
/// Docs: https://docs.hitpayapp.com/apis/guide/online-payments
/// </summary>
public sealed class HitPayProvider : IPaymentProvider
{
    public const string SandboxApi = "https://api.sandbox.hit-pay.com";
    public const string LiveApi = "https://api.hit-pay.com";

    private readonly HttpClient _http;

    public HitPayProvider(HttpClient http) => _http = http;

    public PaymentProviderDescriptor Descriptor { get; } = new()
    {
        Id = PaymentProviderIds.HitPay,
        Name = "HitPay",
        Region = "Malaysia: FPX online banking, DuitNow, Touch 'n Go, GrabPay and cards",
        Currencies = ["MYR", "SGD"],
        ApiKeyLabel = "API key",
        SecretLabel = "Salt",
        WebhookSecretLabel = null,
        SandboxDashboardUrl = "https://dashboard.sandbox.hit-pay.com",
        LiveDashboardUrl = "https://dashboard.hit-pay.com",
        WebhookSetup =
            "In the HitPay dashboard open Developers → Webhook Endpoints → New Webhook, paste this URL and " +
            "subscribe to the event payment_request.completed. HitPay signs it with your salt."
    };

    public async Task<CheckoutSession> CreateCheckoutAsync(
        PaymentAccountCredentials account,
        CheckoutRequest request,
        CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("amount", request.Amount.ToString("0.00", CultureInfo.InvariantCulture)),
            new("currency", request.Currency.ToUpperInvariant()),
            new("purpose", Truncate(request.Description, 255)),
            new("reference_number", request.Reference),
            new("redirect_url", request.ReturnUrl)
        };

        if (!string.IsNullOrWhiteSpace(request.CustomerEmail))
        {
            form.Add(new("email", request.CustomerEmail));
        }

        if (!string.IsNullOrWhiteSpace(request.CustomerName))
        {
            form.Add(new("name", Truncate(request.CustomerName, 100)));
        }

        using var message = Request(HttpMethod.Post, account, "/v1/payment-requests");
        message.Content = new FormUrlEncodedContent(form);

        using var document = await ProviderHttp.SendAsync(_http, message, Descriptor.Name, cancellationToken);
        var root = document.RootElement;

        var id = ProviderHttp.String(root, "id");
        var url = ProviderHttp.String(root, "url");

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(url))
        {
            throw new PaymentProviderException("HitPay did not return a payment page.");
        }

        return new CheckoutSession(id, url);
    }

    public async Task<CheckoutStatus> GetStatusAsync(
        PaymentAccountCredentials account,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        using var message = Request(HttpMethod.Get, account, $"/v1/payment-requests/{Uri.EscapeDataString(sessionId)}");
        using var document = await ProviderHttp.SendAsync(_http, message, Descriptor.Name, cancellationToken);

        return ParseStatus(document.RootElement);
    }

    /// <summary>A payment request as HitPay returns it (API or webhook body).</summary>
    internal static CheckoutStatus ParseStatus(JsonElement root)
    {
        var state = ProviderHttp.String(root, "status")?.ToLowerInvariant() switch
        {
            "completed" => CheckoutState.Paid,
            "failed" or "expired" or "canceled" or "cancelled" => CheckoutState.Failed,
            _ => CheckoutState.Pending
        };

        decimal? amount = decimal.TryParse(
            ProviderHttp.String(root, "amount"),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;

        string? paymentId = null;

        if (root.TryGetProperty("payments", out var payments) &&
            payments.ValueKind == JsonValueKind.Array &&
            payments.GetArrayLength() > 0)
        {
            paymentId = ProviderHttp.String(payments[0], "id");
        }

        return new CheckoutStatus(
            state,
            amount,
            ProviderHttp.String(root, "currency")?.ToUpperInvariant(),
            ProviderHttp.String(root, "reference_number"),
            paymentId);
    }

    public async Task<string?> VerifyCredentialsAsync(
        PaymentAccountCredentials account,
        CancellationToken cancellationToken = default)
    {
        // The cheapest authenticated call; HitPay answers 404 "Invalid business api key." to a wrong key.
        using var message = Request(HttpMethod.Get, account, "/v1/payment-requests?page=1&per_page=1");

        HttpResponseMessage response;

        try
        {
            response = await _http.SendAsync(message, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return "HitPay could not be reached. Try again in a moment.";
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                // The salt cannot be checked by an API call; it is checked on the first webhook.
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            return response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound
                ? $"the API key was not accepted. Check it is the {(account.Live ? "live" : "sandbox")} key."
                : ProviderHttp.ErrorMessage(body) ?? $"HitPay answered {(int)response.StatusCode}.";
        }
    }

    public WebhookNotice? ReadWebhook(
        PaymentAccountCredentials account,
        IReadOnlyDictionary<string, string> headers,
        string body)
    {
        var expected = HMACSHA256.HashData(ProviderHttp.Utf8(account.Secret), ProviderHttp.Utf8(body));

        if (!ProviderHttp.HexEquals(expected, ProviderHttp.Header(headers, "Hitpay-Signature")))
        {
            return null;
        }

        var eventObject = ProviderHttp.Header(headers, "Hitpay-Event-Object");

        if (eventObject is not null && !string.Equals(eventObject, "payment_request", StringComparison.OrdinalIgnoreCase))
        {
            return new WebhookNotice(null);
        }

        try
        {
            using var document = JsonDocument.Parse(body);

            return new WebhookNotice(ProviderHttp.String(document.RootElement, "id"));
        }
        catch (JsonException)
        {
            return new WebhookNotice(null);
        }
    }

    private static HttpRequestMessage Request(HttpMethod method, PaymentAccountCredentials account, string path)
    {
        var message = new HttpRequestMessage(method, (account.Live ? LiveApi : SandboxApi) + path);
        message.Headers.Add("X-BUSINESS-API-KEY", account.ApiKey);
        message.Headers.Add("X-Requested-With", "XMLHttpRequest");

        return message;
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
}
