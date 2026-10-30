using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IronForge.Application.Gym.Billing.Payments;
using IronForge.Payments.Safepay;

namespace IronForge.Application.Tests.Payments;

public class SafepayProviderTests
{
    private static readonly PaymentAccountCredentials Sandbox =
        new("sec_00000000-0000-0000-0000-000000000001", "secret-key", "webhook-secret", Live: false);

    private const string TrackerResponse =
        """{"data":{"tracker":{"token":"track_abc","state":"TRACKER_STARTED"}},"status":{"errors":[],"message":"success"}}""";

    [Fact]
    public async Task CreateCheckout_OpensASessionInPaisa_AndBuildsTheHostedCheckoutUrl()
    {
        var http = new FakeHttp()
            .Respond(HttpStatusCode.Created, TrackerResponse)
            .Respond(HttpStatusCode.OK, """{"data":"tbt+token=="}""");

        var session = await new SafepayProvider(http.Client()).CreateCheckoutAsync(Sandbox, Request());

        Assert.Equal("track_abc", session.SessionId);
        Assert.Equal(
            "https://sandbox.api.getsafepay.com/embedded/?environment=sandbox&tracker=track_abc&tbt=tbt%2Btoken%3D%3D&source=hosted" +
            "&redirect_url=https%3A%2F%2Fapi.test%2Fpayments%2Freturn%2F7%2F42&cancel_url=https%3A%2F%2Fapi.test%2Fpayments%2Fcancelled",
            session.Url);

        var (setup, setupBody) = http.Requests[0];
        Assert.Equal("https://sandbox.api.getsafepay.com/order/payments/v3/", setup.RequestUri!.ToString());
        Assert.Equal("secret-key", setup.Headers.GetValues("X-SFPY-MERCHANT-SECRET").Single());

        using var json = JsonDocument.Parse(setupBody!);
        Assert.Equal(Sandbox.ApiKey, json.RootElement.GetProperty("merchant_api_key").GetString());
        Assert.Equal(500050, json.RootElement.GetProperty("amount").GetInt64());
        Assert.Equal("PKR", json.RootElement.GetProperty("currency").GetString());
        Assert.Equal("7:42", json.RootElement.GetProperty("metadata").GetProperty("order_id").GetString());

        Assert.Equal("https://sandbox.api.getsafepay.com/client/passport/v1/token", http.Requests[1].Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task LiveKeys_UseTheProductionHosts()
    {
        var http = new FakeHttp()
            .Respond(HttpStatusCode.Created, TrackerResponse)
            .Respond(HttpStatusCode.OK, """{"data":"tbt"}""");

        var session = await new SafepayProvider(http.Client()).CreateCheckoutAsync(Sandbox with { Live = true }, Request());

        Assert.StartsWith("https://api.getsafepay.com/", http.Requests[0].Request.RequestUri!.ToString());
        Assert.StartsWith("https://getsafepay.com/embedded/?environment=production&", session.Url);
    }

    [Fact]
    public async Task Status_TrackerEndedIsPaid_WithAmountFromPaisa()
    {
        var http = new FakeHttp().Respond(HttpStatusCode.OK,
            """
            {"ok":true,"data":{"token":"track_abc","state":"TRACKER_ENDED",
             "metadata":{"order_id":{"key":"order_id","value":"7:42"}},
             "purchase_totals":{"quote_amount":{"currency":"PKR","amount":500050}}}}
            """);

        var status = await new SafepayProvider(http.Client()).GetStatusAsync(Sandbox, "track_abc");

        Assert.Equal(new CheckoutStatus(CheckoutState.Paid, 5000.50m, "PKR", "7:42", "track_abc"), status);
        Assert.Equal("https://sandbox.api.getsafepay.com/reporter/api/v1/payments/track_abc", http.Requests[0].Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task Status_StartedIsPending()
    {
        var http = new FakeHttp().Respond(HttpStatusCode.OK,
            """{"data":{"token":"track_abc","state":"TRACKER_STARTED","purchase_totals":{"quote_amount":{"currency":"PKR","amount":100}}}}""");

        Assert.Equal(CheckoutState.Pending, (await new SafepayProvider(http.Client()).GetStatusAsync(Sandbox, "track_abc")).State);
    }

    [Fact]
    public async Task VerifyCredentials_ChecksTheSecretWithThePassport()
    {
        // Safepay opens sessions for any secret, so the passport call is the check.
        var wrongSecret = new FakeHttp().Respond(HttpStatusCode.Unauthorized,
            """{"data":null,"status":{"errors":["unauthorized"],"message":"fail"}}""");

        Assert.Equal("the secret key was not accepted.", await new SafepayProvider(wrongSecret.Client()).VerifyCredentialsAsync(Sandbox));

        var wrongPublicKey = new FakeHttp()
            .Respond(HttpStatusCode.OK, """{"data":"tbt"}""")
            .Respond(HttpStatusCode.BadRequest, """{"data":null,"status":{"errors":["merchant_api_key: must be a valid UUID."],"message":"fail"}}""");

        Assert.Equal("merchant_api_key: must be a valid UUID.", await new SafepayProvider(wrongPublicKey.Client()).VerifyCredentialsAsync(Sandbox));

        var ok = new FakeHttp()
            .Respond(HttpStatusCode.OK, """{"data":"tbt"}""")
            .Respond(HttpStatusCode.Created, TrackerResponse);

        Assert.Null(await new SafepayProvider(ok.Client()).VerifyCredentialsAsync(Sandbox));
    }

    [Theory]
    [InlineData(1.00, 100)]
    [InlineData(5000.50, 500050)]
    [InlineData(0.015, 2)]
    public void Amounts_GoToSafepayInTheLowestUnit(decimal amount, long minor)
    {
        Assert.Equal(minor, SafepayProvider.ToMinorUnits(amount));
    }

    [Fact]
    public void Webhook_NeedsTheEndpointsSharedSecret_Sha512()
    {
        const string body = """{"type":"payment.succeeded","data":{"tracker":{"token":"track_abc"},"success":true}}""";
        var provider = new SafepayProvider(new HttpClient());

        var overBody = Sign(body);
        Assert.Equal("track_abc", provider.ReadWebhook(Sandbox, Headers(overBody), body)!.SessionId);

        // Safepay's guide signs "the payload": its data object is accepted too.
        var overData = Sign("""{"tracker":{"token":"track_abc"},"success":true}""");
        Assert.Equal("track_abc", provider.ReadWebhook(Sandbox, Headers(overData), body)!.SessionId);

        Assert.Null(provider.ReadWebhook(Sandbox, Headers(overBody), body.Replace("true", "false")));
        Assert.Null(provider.ReadWebhook(Sandbox with { WebhookSecret = null }, Headers(overBody), body));
        Assert.Null(provider.ReadWebhook(Sandbox, new Dictionary<string, string>(), body));
    }

    private static string Sign(string payload) =>
        Convert.ToHexStringLower(HMACSHA512.HashData(Encoding.UTF8.GetBytes("webhook-secret"), Encoding.UTF8.GetBytes(payload)));

    private static Dictionary<string, string> Headers(string signature) =>
        new(StringComparer.OrdinalIgnoreCase) { ["X-SFPY-SIGNATURE"] = signature };

    private static CheckoutRequest Request() => new()
    {
        TenantId = 7,
        InvoiceId = 42,
        InvoiceNumber = "INV-000042",
        Description = "Iron Paradise: Monthly (INV-000042)",
        Amount = 5000.50m,
        Currency = "PKR",
        Reference = "7:42",
        CustomerEmail = "ali@example.com",
        ReturnUrl = "https://api.test/payments/return/7/42",
        CancelUrl = "https://api.test/payments/cancelled"
    };
}
