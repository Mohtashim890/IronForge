using IronForge.Application.Gym.Billing.Payments;
using IronForge.Payments.HitPay;
using IronForge.Payments.Safepay;

namespace IronForge.Application.Tests.Payments;

/// <summary>Runs only when the named environment variables are set; otherwise the test is skipped.</summary>
public sealed class SandboxFactAttribute : FactAttribute
{
    public SandboxFactAttribute(params string[] variables)
    {
        var missing = variables.Where(x => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(x))).ToList();

        if (missing.Count > 0)
        {
            Skip = $"Set {string.Join(", ", missing)} to run against the provider's sandbox.";
        }
    }
}

/// <summary>
/// Calls the providers' real sandboxes with keys from the environment (never
/// from the repository). Run before releasing a provider change:
///   IRONFORGE_HITPAY_SANDBOX_KEY, IRONFORGE_SAFEPAY_SANDBOX_PUBLIC_KEY, IRONFORGE_SAFEPAY_SANDBOX_SECRET
/// </summary>
public class ProviderSandboxTests
{
    private const string HitPayKey = "IRONFORGE_HITPAY_SANDBOX_KEY";
    private const string SafepayPublicKey = "IRONFORGE_SAFEPAY_SANDBOX_PUBLIC_KEY";
    private const string SafepaySecret = "IRONFORGE_SAFEPAY_SANDBOX_SECRET";

    [SandboxFact(HitPayKey)]
    public async Task HitPay_AcceptsTheKey_CreatesACheckout_AndReportsItPending()
    {
        var provider = new HitPayProvider(new HttpClient());
        var account = new PaymentAccountCredentials(Env(HitPayKey), "salt-not-used-here", null, Live: false);

        Assert.Null(await provider.VerifyCredentialsAsync(account));
        Assert.NotNull(await provider.VerifyCredentialsAsync(account with { ApiKey = "test_wrong" }));

        var reference = $"sandbox:{Guid.NewGuid():N}";
        var session = await provider.CreateCheckoutAsync(account, Request("MYR", 1.50m, reference));

        Assert.StartsWith("https://checkout.sandbox.hit-pay.com/", session.Url);

        var status = await provider.GetStatusAsync(account, session.SessionId);

        Assert.Equal(new CheckoutStatus(CheckoutState.Pending, 1.50m, "MYR", reference, null), status);
    }

    [SandboxFact(SafepayPublicKey, SafepaySecret)]
    public async Task Safepay_AcceptsTheKeys_CreatesACheckout_AndReportsItPending()
    {
        var provider = new SafepayProvider(new HttpClient());
        var account = new PaymentAccountCredentials(Env(SafepayPublicKey), Env(SafepaySecret), null, Live: false);

        Assert.Null(await provider.VerifyCredentialsAsync(account));
        Assert.Equal("the secret key was not accepted.", await provider.VerifyCredentialsAsync(account with { Secret = "wrong" }));

        var reference = $"sandbox:{Guid.NewGuid():N}";
        var session = await provider.CreateCheckoutAsync(account, Request("PKR", 1500m, reference));

        Assert.StartsWith("https://sandbox.api.getsafepay.com/embedded/?environment=sandbox&tracker=track_", session.Url);

        using (var page = await new HttpClient().GetAsync(session.Url))
        {
            Assert.True(page.IsSuccessStatusCode, $"The checkout page answered {(int)page.StatusCode}.");
        }

        var status = await provider.GetStatusAsync(account, session.SessionId);

        Assert.Equal(CheckoutState.Pending, status.State);
        Assert.Equal(1500m, status.Amount);
        Assert.Equal("PKR", status.Currency);
        Assert.Equal(reference, status.Reference);
    }

    private static string Env(string name) => Environment.GetEnvironmentVariable(name)!;

    private static CheckoutRequest Request(string currency, decimal amount, string reference) => new()
    {
        TenantId = 1,
        InvoiceId = 1,
        InvoiceNumber = "INV-SANDBOX",
        Description = "IronForge sandbox test",
        Amount = amount,
        Currency = currency,
        Reference = reference,
        CustomerEmail = "sandbox@example.com",
        CustomerName = "Sandbox Test",
        ReturnUrl = "https://localhost:7226/payments/return/1/1",
        CancelUrl = "https://localhost:7226/payments/cancelled"
    };
}
