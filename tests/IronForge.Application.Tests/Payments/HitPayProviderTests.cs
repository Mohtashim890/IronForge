using System.Net;
using System.Security.Cryptography;
using System.Text;
using IronForge.Application.Gym.Billing.Payments;
using IronForge.Payments.HitPay;

namespace IronForge.Application.Tests.Payments;

public class HitPayProviderTests
{
    private static readonly PaymentAccountCredentials Sandbox = new("test_key", "the-salt", null, Live: false);

    [Fact]
    public async Task CreateCheckout_PostsAFormWithTheReference_ToTheSandboxOrLiveApi()
    {
        var http = new FakeHttp().Respond(HttpStatusCode.Created,
            """{"id":"pr_1","url":"https://checkout.sandbox.hit-pay.com/payment-request/@gym/pr_1/checkout","status":"pending"}""");

        var session = await new HitPayProvider(http.Client()).CreateCheckoutAsync(Sandbox, Request());

        Assert.Equal("pr_1", session.SessionId);
        Assert.Equal("https://checkout.sandbox.hit-pay.com/payment-request/@gym/pr_1/checkout", session.Url);

        var (request, body) = Assert.Single(http.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.sandbox.hit-pay.com/v1/payment-requests", request.RequestUri!.ToString());
        Assert.Equal("test_key", request.Headers.GetValues("X-BUSINESS-API-KEY").Single());
        Assert.Contains("amount=150.00", body);
        Assert.Contains("currency=MYR", body);
        Assert.Contains("reference_number=7%3A42", body);
        Assert.Contains("redirect_url=https%3A%2F%2Fapi.test%2Fpayments%2Freturn%2F7%2F42", body);
        Assert.Contains("email=ali%40example.com", body);

        var live = new FakeHttp().Respond(HttpStatusCode.Created, """{"id":"pr_2","url":"https://x"}""");
        await new HitPayProvider(live.Client()).CreateCheckoutAsync(Sandbox with { Live = true }, Request());
        Assert.StartsWith("https://api.hit-pay.com/", live.Requests[0].Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task Status_CompletedIsPaid_WithAmountCurrencyAndReference()
    {
        var http = new FakeHttp().Respond(HttpStatusCode.OK,
            """
            {"id":"pr_1","status":"completed","amount":"150.00","currency":"myr","reference_number":"7:42",
             "payments":[{"id":"pay_9","status":"succeeded"}]}
            """);

        var status = await new HitPayProvider(http.Client()).GetStatusAsync(Sandbox, "pr_1");

        Assert.Equal(new CheckoutStatus(CheckoutState.Paid, 150.00m, "MYR", "7:42", "pay_9"), status);
        Assert.Equal("https://api.sandbox.hit-pay.com/v1/payment-requests/pr_1", http.Requests[0].Request.RequestUri!.ToString());
    }

    [Theory]
    [InlineData("pending", CheckoutState.Pending)]
    [InlineData("expired", CheckoutState.Failed)]
    [InlineData("failed", CheckoutState.Failed)]
    public async Task Status_MapsHitPayStates(string hitPayStatus, CheckoutState expected)
    {
        var http = new FakeHttp().Respond(HttpStatusCode.OK, $$"""{"id":"pr_1","status":"{{hitPayStatus}}","amount":"1.00","currency":"MYR"}""");

        Assert.Equal(expected, (await new HitPayProvider(http.Client()).GetStatusAsync(Sandbox, "pr_1")).State);
    }

    [Fact]
    public async Task ProviderErrors_BecomeReadableExceptions()
    {
        var http = new FakeHttp().Respond(HttpStatusCode.UnprocessableEntity, """{"message":"The amount must be at least 1."}""");

        var exception = await Assert.ThrowsAsync<PaymentProviderException>(
            () => new HitPayProvider(http.Client()).CreateCheckoutAsync(Sandbox, Request()));

        Assert.Equal("The amount must be at least 1.", exception.Message);
        Assert.Equal(422, exception.StatusCode);
    }

    [Fact]
    public async Task VerifyCredentials_ExplainsAWrongKey()
    {
        var ok = new FakeHttp().Respond(HttpStatusCode.OK, """{"data":[]}""");
        Assert.Null(await new HitPayProvider(ok.Client()).VerifyCredentialsAsync(Sandbox));

        var wrong = new FakeHttp().Respond(HttpStatusCode.NotFound, """{"message":"Invalid business api key."}""");
        Assert.Contains("sandbox key", await new HitPayProvider(wrong.Client()).VerifyCredentialsAsync(Sandbox));
    }

    [Fact]
    public void Webhook_IsAcceptedOnlyWithTheSaltsSignatureOfTheRawBody()
    {
        const string body = """{"id":"pr_1","status":"completed","amount":"150.00","currency":"MYR","reference_number":"7:42"}""";
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes("the-salt"), Encoding.UTF8.GetBytes(body)));
        var provider = new HitPayProvider(new HttpClient());

        var notice = provider.ReadWebhook(Sandbox, Headers(("Hitpay-Signature", signature), ("Hitpay-Event-Object", "payment_request")), body);
        Assert.Equal("pr_1", notice!.SessionId);

        Assert.Null(provider.ReadWebhook(Sandbox, Headers(("Hitpay-Signature", signature)), body.Replace("150.00", "1.00")));
        Assert.Null(provider.ReadWebhook(Sandbox with { Secret = "other-salt" }, Headers(("Hitpay-Signature", signature)), body));
        Assert.Null(provider.ReadWebhook(Sandbox, Headers(), body));

        // Signed, but about something else (e.g. a refund): acknowledged, no checkout.
        var refund = provider.ReadWebhook(Sandbox, Headers(("hitpay-signature", signature), ("Hitpay-Event-Object", "refund")), body);
        Assert.Null(refund!.SessionId);
    }

    private static CheckoutRequest Request() => new()
    {
        TenantId = 7,
        InvoiceId = 42,
        InvoiceNumber = "INV-000042",
        Description = "Iron Gym: Monthly (INV-000042)",
        Amount = 150m,
        Currency = "MYR",
        Reference = "7:42",
        CustomerEmail = "ali@example.com",
        CustomerName = "Ali",
        ReturnUrl = "https://api.test/payments/return/7/42",
        CancelUrl = "https://api.test/payments/cancelled"
    };

    private static Dictionary<string, string> Headers(params (string Name, string Value)[] headers) =>
        headers.ToDictionary(x => x.Name, x => x.Value, StringComparer.OrdinalIgnoreCase);
}
