using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using IronForge.Application.Gym.Billing.Payments;

namespace IronForge.Payments.Safepay;

/// <summary>
/// Safepay (Pakistan, SBP-licensed gateway): hosted "embedded" checkout.
/// - Session: POST /order/payments/v3/ (X-SFPY-MERCHANT-SECRET: secret key,
///   merchant_api_key: public key, amount in the lowest unit) → tracker.
/// - Passport: POST /client/passport/v1/token → time-bound token (1 hour).
/// - Checkout page: {host}/embedded/?environment&amp;tracker&amp;tbt&amp;source&amp;redirect_url&amp;cancel_url.
/// - Status: GET /reporter/api/v1/payments/{tracker}; TRACKER_ENDED means paid.
/// - Webhooks: registered in the dashboard (Developers → Endpoints); signed
///   with HMAC-SHA512 (hex) keyed with the endpoint's shared secret, in X-SFPY-SIGNATURE.
/// Docs: https://safepay-docs.netlify.app ; flow as in the official SDK @sfpy/node-core.
/// </summary>
public sealed class SafepayProvider : IPaymentProvider
{
    public const string SandboxApi = "https://sandbox.api.getsafepay.com";
    public const string LiveApi = "https://api.getsafepay.com";
    public const string SandboxCheckout = "https://sandbox.api.getsafepay.com/embedded/";
    public const string LiveCheckout = "https://getsafepay.com/embedded/";

    private readonly HttpClient _http;

    public SafepayProvider(HttpClient http) => _http = http;

    public PaymentProviderDescriptor Descriptor { get; } = new()
    {
        Id = PaymentProviderIds.Safepay,
        Name = "Safepay",
        Region = "Pakistan: cards and the payment methods enabled on your Safepay account",
        Currencies = ["PKR", "USD"],
        ApiKeyLabel = "Public key",
        SecretLabel = "Secret key",
        WebhookSecretLabel = "Webhook shared secret",
        SandboxDashboardUrl = "https://sandbox.api.getsafepay.com/dashboard/developers/api",
        LiveDashboardUrl = "https://getsafepay.com/dashboard/developers/api",
        WebhookSetup =
            "In the Safepay dashboard open Developer → Endpoints → Add an endpoint, paste this URL, subscribe to " +
            "payment.succeeded (2.0.0 events), then copy the endpoint's shared secret into the field above."
    };

    public async Task<CheckoutSession> CreateCheckoutAsync(
        PaymentAccountCredentials account,
        CheckoutRequest request,
        CancellationToken cancellationToken = default)
    {
        var tracker = await CreateTrackerAsync(account, request.Currency, request.Amount, request.Reference, cancellationToken);
        var passport = await CreatePassportAsync(account, cancellationToken);

        var query = new Dictionary<string, string>
        {
            ["environment"] = account.Live ? "production" : "sandbox",
            ["tracker"] = tracker,
            ["tbt"] = passport,
            ["source"] = "hosted",
            ["redirect_url"] = request.ReturnUrl,
            ["cancel_url"] = request.CancelUrl
        };

        var url = (account.Live ? LiveCheckout : SandboxCheckout) + "?" +
                  string.Join("&", query.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));

        return new CheckoutSession(tracker, url);
    }

    public async Task<CheckoutStatus> GetStatusAsync(
        PaymentAccountCredentials account,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        using var message = Request(HttpMethod.Get, account, $"/reporter/api/v1/payments/{Uri.EscapeDataString(sessionId)}");
        using var document = await ProviderHttp.SendAsync(_http, message, Descriptor.Name, cancellationToken);

        if (!document.RootElement.TryGetProperty("data", out var data))
        {
            throw new PaymentProviderException("Safepay did not return the payment.");
        }

        return ParseTracker(data, sessionId);
    }

    /// <summary>A tracker as the reporter API returns it ("data").</summary>
    internal static CheckoutStatus ParseTracker(JsonElement data, string sessionId)
    {
        var state = ProviderHttp.String(data, "state") switch
        {
            "TRACKER_ENDED" => CheckoutState.Paid,
            "TRACKER_CANCELLED" or "TRACKER_FAILED" or "TRACKER_EXPIRED" => CheckoutState.Failed,
            _ => CheckoutState.Pending
        };

        var currency = ProviderHttp.String(data, "purchase_totals", "quote_amount", "currency");

        decimal? amount = long.TryParse(
            ProviderHttp.String(data, "purchase_totals", "quote_amount", "amount"),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var minor)
            ? FromMinorUnits(minor)
            : null;

        return new CheckoutStatus(
            state,
            amount,
            currency?.ToUpperInvariant(),
            ProviderHttp.String(data, "metadata", "order_id", "value"),
            ProviderHttp.String(data, "token") ?? sessionId);
    }

    public async Task<string?> VerifyCredentialsAsync(
        PaymentAccountCredentials account,
        CancellationToken cancellationToken = default)
    {
        // A session checks the public key and the passport the secret key (a session alone
        // accepts any secret). Neither charges anything.
        try
        {
            await CreatePassportAsync(account, cancellationToken);
            await CreateTrackerAsync(account, "PKR", 1m, "ironforge-credentials-check", cancellationToken);

            return null;
        }
        catch (PaymentProviderException exception)
        {
            return exception.Message;
        }
    }

    public WebhookNotice? ReadWebhook(
        PaymentAccountCredentials account,
        IReadOnlyDictionary<string, string> headers,
        string body)
    {
        if (string.IsNullOrWhiteSpace(account.WebhookSecret))
        {
            return null;
        }

        var signature = ProviderHttp.Header(headers, "X-SFPY-SIGNATURE");
        var key = ProviderHttp.Utf8(account.WebhookSecret);

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            // Safepay's guide signs "the payload": accept the raw body, or its "data" object as sent.
            var signed =
                ProviderHttp.HexEquals(HMACSHA512.HashData(key, ProviderHttp.Utf8(body)), signature) ||
                (document.RootElement.ValueKind == JsonValueKind.Object &&
                 document.RootElement.TryGetProperty("data", out var data) &&
                 ProviderHttp.HexEquals(HMACSHA512.HashData(key, ProviderHttp.Utf8(data.GetRawText())), signature));

            if (!signed)
            {
                return null;
            }

            return new WebhookNotice(FindTracker(document.RootElement));
        }
    }

    /// <summary>The tracker token anywhere in a webhook body (e.g. data.tracker.token).</summary>
    internal static string? FindTracker(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var value = element.GetString();
                return value is not null && value.StartsWith("track_", StringComparison.Ordinal) ? value : null;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (FindTracker(property.Value) is { } found)
                    {
                        return found;
                    }
                }

                return null;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (FindTracker(item) is { } found)
                    {
                        return found;
                    }
                }

                return null;

            default:
                return null;
        }
    }

    private async Task<string> CreateTrackerAsync(
        PaymentAccountCredentials account,
        string currency,
        decimal amount,
        string reference,
        CancellationToken cancellationToken)
    {
        using var message = Request(HttpMethod.Post, account, "/order/payments/v3/");
        message.Content = JsonContent.Create(new Dictionary<string, object>
        {
            ["merchant_api_key"] = account.ApiKey,
            ["intent"] = "CYBERSOURCE",
            ["mode"] = "payment",
            ["currency"] = currency.ToUpperInvariant(),
            ["amount"] = ToMinorUnits(amount),
            ["metadata"] = new Dictionary<string, string> { ["order_id"] = reference }
        });

        using var document = await ProviderHttp.SendAsync(_http, message, Descriptor.Name, cancellationToken);

        return ProviderHttp.String(document.RootElement, "data", "tracker", "token")
               ?? throw new PaymentProviderException("Safepay did not return a payment session.");
    }

    private async Task<string> CreatePassportAsync(PaymentAccountCredentials account, CancellationToken cancellationToken)
    {
        using var message = Request(HttpMethod.Post, account, "/client/passport/v1/token");
        message.Content = JsonContent.Create(new { });

        try
        {
            using var document = await ProviderHttp.SendAsync(_http, message, Descriptor.Name, cancellationToken);

            return ProviderHttp.String(document.RootElement, "data")
                   ?? throw new PaymentProviderException("Safepay did not return a checkout token.");
        }
        catch (PaymentProviderException exception) when (exception.StatusCode is 401 or 403)
        {
            throw new PaymentProviderException("the secret key was not accepted.", exception, exception.StatusCode);
        }
    }

    /// <summary>PKR and USD amounts go to Safepay in the lowest unit (paisa, cents).</summary>
    internal static long ToMinorUnits(decimal amount) =>
        (long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);

    internal static decimal FromMinorUnits(long minor) => minor / 100m;

    private static HttpRequestMessage Request(HttpMethod method, PaymentAccountCredentials account, string path)
    {
        var message = new HttpRequestMessage(method, (account.Live ? LiveApi : SandboxApi) + path);
        message.Headers.Add("X-SFPY-MERCHANT-SECRET", account.Secret);

        return message;
    }
}
