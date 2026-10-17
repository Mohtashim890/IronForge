using System.Globalization;
using IronForge.Application.Configurations;
using IronForge.Application.Gym.Billing;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace IronForge.Infrastructure.Payments;

/// <summary>
/// Stripe Checkout for member invoices. With a connected account (Stripe
/// Connect, Standard) the charge is created directly on the gym's account;
/// otherwise on the platform account (test mode and demos).
/// </summary>
public sealed class StripePaymentGateway : IPaymentGateway
{
    /// <summary>Currencies Stripe expects in whole units, not cents.</summary>
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "BIF", "CLP", "DJF", "GNF", "JPY", "KMF", "KRW", "MGA", "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF"
    };

    private readonly StripeClient _client;
    private readonly AppUrlOptions _urls;

    public StripePaymentGateway(IOptions<StripeOptions> options, IOptions<AppUrlOptions> urls)
    {
        _client = new StripeClient(options.Value.SecretKey);
        _urls = urls.Value;
    }

    public string Provider => "stripe";

    public bool IsSimulated => false;

    public async Task<CheckoutResult> CreateCheckoutAsync(
        CheckoutRequest request,
        CancellationToken cancellationToken = default)
    {
        var metadata = new Dictionary<string, string>
        {
            ["tenant_id"] = request.TenantId.ToString(CultureInfo.InvariantCulture),
            ["invoice_id"] = request.InvoiceId.ToString(CultureInfo.InvariantCulture),
            ["invoice_number"] = request.InvoiceNumber
        };

        var options = new SessionCreateOptions
        {
            Mode = "payment",
            ClientReferenceId = $"{request.TenantId}:{request.InvoiceId}",
            CustomerEmail = request.CustomerEmail,
            Metadata = metadata,
            PaymentIntentData = new SessionPaymentIntentDataOptions { Metadata = metadata },
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = request.Currency.ToLowerInvariant(),
                        UnitAmount = ToMinorUnits(request.Amount, request.Currency),
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = $"Invoice {request.InvoiceNumber}",
                            Description = request.Description
                        }
                    }
                }
            ],
            SuccessUrl = $"{_urls.ApiBaseUrl.TrimEnd('/')}/payments/complete?status=success",
            CancelUrl = $"{_urls.ApiBaseUrl.TrimEnd('/')}/payments/complete?status=cancelled",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        var requestOptions = string.IsNullOrWhiteSpace(request.ConnectedAccountId)
            ? null
            : new RequestOptions { StripeAccount = request.ConnectedAccountId };

        var session = await new SessionService(_client).CreateAsync(
            options,
            requestOptions,
            cancellationToken);

        return new CheckoutResult(session.Id, session.Url);
    }

    public async Task<ConnectOnboardingLink> CreateConnectOnboardingLinkAsync(
        string? existingAccountId,
        string? email,
        CancellationToken cancellationToken = default)
    {
        var accountId = existingAccountId;

        if (string.IsNullOrWhiteSpace(accountId))
        {
            var account = await new AccountService(_client).CreateAsync(
                new AccountCreateOptions { Type = "standard", Email = email },
                cancellationToken: cancellationToken);

            accountId = account.Id;
        }

        var settingsUrl = $"{_urls.WebBaseUrl.TrimEnd('/')}/settings";

        var link = await new AccountLinkService(_client).CreateAsync(
            new AccountLinkCreateOptions
            {
                Account = accountId,
                Type = "account_onboarding",
                RefreshUrl = $"{settingsUrl}?stripe=refresh",
                ReturnUrl = $"{settingsUrl}?stripe=return"
            },
            cancellationToken: cancellationToken);

        return new ConnectOnboardingLink(accountId, link.Url);
    }

    public async Task<bool> IsChargesEnabledAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        var account = await new AccountService(_client).GetAsync(
            accountId,
            cancellationToken: cancellationToken);

        return account.ChargesEnabled;
    }

    public static long ToMinorUnits(decimal amount, string currency) =>
        ZeroDecimalCurrencies.Contains(currency)
            ? (long)decimal.Round(amount, 0, MidpointRounding.AwayFromZero)
            : (long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);

    public static decimal FromMinorUnits(long amount, string currency) =>
        ZeroDecimalCurrencies.Contains(currency) ? amount : amount / 100m;
}
