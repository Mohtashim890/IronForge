using System.Globalization;
using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Commons;
using IronForge.Application.Configurations;
using IronForge.Application.Entities;
using IronForge.Application.Gym.Common;
using IronForge.Application.Persistence;
using IronForge.Shared.Models.Gym;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IronForge.Application.Gym.Billing.Payments;

/// <summary>
/// Online payment of member invoices through the gym's own provider
/// account (HitPay, Safepay), or the simulated checkout where the platform
/// allows it. An invoice is marked paid only on what the provider's API
/// says about the checkout: a webhook, the payer's return to IronForge and
/// the app's "I've paid" button all just trigger that check. The caller has
/// authorized the action and set the tenant execution context.
/// </summary>
public interface IOnlinePaymentService
{
    /// <summary>Whether members of this gym can pay online now.</summary>
    Task<bool> IsAvailableAsync(GymSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a checkout for the invoice and records it on the (tracked)
    /// invoice; the caller saves.
    /// </summary>
    Task<ServiceResult<CheckoutSessionDto>> StartCheckoutAsync(
        GymInfo gym,
        Invoice invoice,
        MemberProfile member,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the provider about the invoice's latest checkout and applies the
    /// payment when it is paid. True when the invoice is paid (now or before).
    /// </summary>
    Task<ServiceResult<bool>> ConfirmInvoiceAsync(
        int tenantId,
        int invoiceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A provider's webhook: the signature is checked with the gym's
    /// credentials, then the checkout's status is fetched from the provider.
    /// </summary>
    Task<ServiceResult<bool>> HandleWebhookAsync(
        string providerId,
        int tenantId,
        IReadOnlyDictionary<string, string> headers,
        string body,
        CancellationToken cancellationToken = default);

    /// <summary>The checkout reference that ties a provider payment to an invoice.</summary>
    static string Reference(int tenantId, int invoiceId) => $"{tenantId}:{invoiceId}";
}

public sealed class OnlinePaymentService : IOnlinePaymentService
{
    private readonly IRepository<GymPaymentAccount> _accounts;
    private readonly IRepository<Invoice> _invoices;
    private readonly IPaymentProviderCatalog _providers;
    private readonly IPaymentCredentialProtector _protector;
    private readonly IPaymentConfirmationService _confirmation;
    private readonly IAuditService _audit;
    private readonly PaymentOptions _options;
    private readonly AppUrlOptions _urls;
    private readonly ILogger<OnlinePaymentService> _logger;

    public OnlinePaymentService(
        IRepository<GymPaymentAccount> accounts,
        IRepository<Invoice> invoices,
        IPaymentProviderCatalog providers,
        IPaymentCredentialProtector protector,
        IPaymentConfirmationService confirmation,
        IAuditService audit,
        IOptions<PaymentOptions> options,
        IOptions<AppUrlOptions> urls,
        ILogger<OnlinePaymentService> logger)
    {
        _accounts = accounts;
        _invoices = invoices;
        _providers = providers;
        _protector = protector;
        _confirmation = confirmation;
        _audit = audit;
        _options = options.Value;
        _urls = urls.Value;
        _logger = logger;
    }

    public async Task<bool> IsAvailableAsync(GymSettings settings, CancellationToken cancellationToken = default) =>
        await ResolveAsync(settings.TenantId, settings.Currency, cancellationToken) is not null;

    public async Task<ServiceResult<CheckoutSessionDto>> StartCheckoutAsync(
        GymInfo gym,
        Invoice invoice,
        MemberProfile member,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAsync(gym.TenantId, invoice.Currency, cancellationToken);

        if (resolved is null)
        {
            return GymResults.Conflict<CheckoutSessionDto>(
                $"{gym.Name} does not take online payments yet. Pay at the front desk or as the gym describes.");
        }

        var (provider, credentials) = resolved.Value;
        var apiBase = _urls.ApiBaseUrl.TrimEnd('/');

        CheckoutSession session;

        try
        {
            session = await provider.CreateCheckoutAsync(
                credentials,
                new CheckoutRequest
                {
                    TenantId = gym.TenantId,
                    InvoiceId = invoice.Id,
                    InvoiceNumber = invoice.Number,
                    Description = $"{gym.Name}: {invoice.Description} ({invoice.Number})",
                    Amount = invoice.Amount,
                    Currency = invoice.Currency,
                    Reference = IOnlinePaymentService.Reference(gym.TenantId, invoice.Id),
                    CustomerEmail = member.Email,
                    CustomerName = member.FullName,
                    // Path-based: providers append their own query string.
                    ReturnUrl = $"{apiBase}/payments/return/{gym.TenantId}/{invoice.Id}",
                    CancelUrl = $"{apiBase}/payments/cancelled"
                },
                cancellationToken);
        }
        catch (PaymentProviderException exception)
        {
            _logger.LogWarning(exception, "Checkout for invoice {InvoiceId} failed at {Provider}.", invoice.Id, provider.Descriptor.Id);

            return ServiceResult<CheckoutSessionDto>.Fail(
                $"The payment page could not be opened ({provider.Descriptor.Name}: {exception.Message}). Try again later or pay at the front desk.",
                502);
        }

        invoice.CheckoutSessionId = session.SessionId;
        invoice.CheckoutProvider = provider.Descriptor.Id;

        return ServiceResult<CheckoutSessionDto>.Ok(new CheckoutSessionDto
        {
            Url = session.Url,
            Provider = provider.Descriptor.Id,
            Simulated = provider.Descriptor.Id == PaymentProviderIds.Simulated
        });
    }

    public async Task<ServiceResult<bool>> ConfirmInvoiceAsync(
        int tenantId,
        int invoiceId,
        CancellationToken cancellationToken = default)
    {
        var invoice = await _invoices.FirstOrDefaultAsync(
            x => x.Id == invoiceId && x.TenantId == tenantId,
            cancellationToken);

        if (invoice is null)
        {
            return GymResults.NotFound<bool>("Invoice");
        }

        if (invoice.Status == InvoiceStatus.Paid)
        {
            return ServiceResult<bool>.Ok(true);
        }

        if (invoice.CheckoutSessionId is null || invoice.CheckoutProvider is null)
        {
            return ServiceResult<bool>.Ok(false);
        }

        // The simulated checkout confirms itself on its own page.
        if (invoice.CheckoutProvider == PaymentProviderIds.Simulated)
        {
            return ServiceResult<bool>.Ok(false);
        }

        var account = await LoadAccountAsync(tenantId, invoice.CheckoutProvider, cancellationToken);

        if (account is null)
        {
            return ServiceResult<bool>.Fail("The gym's payment provider is no longer connected.", 409);
        }

        return await ConfirmSessionAsync(account.Value.Provider, account.Value.Credentials, tenantId, invoice.CheckoutSessionId, invoiceId, cancellationToken);
    }

    public async Task<ServiceResult<bool>> HandleWebhookAsync(
        string providerId,
        int tenantId,
        IReadOnlyDictionary<string, string> headers,
        string body,
        CancellationToken cancellationToken = default)
    {
        var account = await LoadAccountAsync(tenantId, providerId, cancellationToken);

        if (account is null)
        {
            return ServiceResult<bool>.Fail("The gym has no account with this provider.", 404);
        }

        var (provider, credentials) = account.Value;
        var notice = provider.ReadWebhook(credentials, headers, body);

        if (notice is null)
        {
            await _audit.RecordAsync(
                AuditCategory.Billing,
                "PaymentWebhookRejected",
                AuditOutcome.Denied,
                AuditActorType.System,
                "GymPaymentAccount",
                tenantId.ToString(CultureInfo.InvariantCulture),
                reason: $"{provider.Descriptor.Name} webhook with a missing or invalid signature.",
                cancellationToken: cancellationToken,
                tenantId: tenantId);

            return ServiceResult<bool>.Fail("Invalid webhook signature.", 401);
        }

        // Signed, but not about a checkout (e.g. a refund): acknowledged, nothing to do.
        if (notice.SessionId is null)
        {
            return ServiceResult<bool>.Ok(false);
        }

        return await ConfirmSessionAsync(provider, credentials, tenantId, notice.SessionId, expectedInvoiceId: null, cancellationToken);
    }

    /// <summary>
    /// The provider's word on a checkout: it must carry this gym's reference
    /// for the invoice, and the confirmation checks amount and currency.
    /// </summary>
    private async Task<ServiceResult<bool>> ConfirmSessionAsync(
        IPaymentProvider provider,
        PaymentAccountCredentials credentials,
        int tenantId,
        string sessionId,
        int? expectedInvoiceId,
        CancellationToken cancellationToken)
    {
        CheckoutStatus status;

        try
        {
            status = await provider.GetStatusAsync(credentials, sessionId, cancellationToken);
        }
        catch (PaymentProviderException exception)
        {
            _logger.LogWarning(exception, "Status of {Provider} checkout {SessionId} could not be read.", provider.Descriptor.Id, sessionId);

            return ServiceResult<bool>.Fail($"{provider.Descriptor.Name} could not be reached. Try again in a moment.", 502);
        }

        var invoiceId = ParseInvoiceId(tenantId, status.Reference);

        if (invoiceId is null || (expectedInvoiceId is not null && invoiceId != expectedInvoiceId))
        {
            _logger.LogWarning(
                "{Provider} checkout {SessionId} has reference {Reference}, not an invoice of tenant {TenantId}.",
                provider.Descriptor.Id,
                sessionId,
                status.Reference,
                tenantId);

            return ServiceResult<bool>.Fail("The payment does not belong to this invoice.", 400);
        }

        if (status.State != CheckoutState.Paid)
        {
            return ServiceResult<bool>.Ok(false);
        }

        if (status.Amount is null || string.IsNullOrWhiteSpace(status.Currency))
        {
            return ServiceResult<bool>.Fail("The provider did not report the paid amount.", 502);
        }

        var confirmed = await _confirmation.ConfirmCheckoutAsync(
            provider.Descriptor.Id,
            $"{provider.Descriptor.Id}:{sessionId}",
            tenantId,
            invoiceId.Value,
            status.PaymentReference ?? sessionId,
            status.Amount.Value,
            status.Currency,
            cancellationToken);

        if (!confirmed.Success)
        {
            return confirmed;
        }

        // Applied now, or already handled earlier (another webhook or the return page).
        var invoice = await _invoices.FirstOrDefaultAsync(x => x.Id == invoiceId.Value, cancellationToken);

        return ServiceResult<bool>.Ok(invoice?.Status == InvoiceStatus.Paid);
    }

    private static int? ParseInvoiceId(int tenantId, string? reference)
    {
        var parts = (reference ?? "").Split(':');

        return parts.Length == 2 &&
               parts[0] == tenantId.ToString(CultureInfo.InvariantCulture) &&
               int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var invoiceId)
            ? invoiceId
            : null;
    }

    /// <summary>The gym's connected provider when it charges in this currency; otherwise the simulated one where allowed.</summary>
    private async Task<(IPaymentProvider Provider, PaymentAccountCredentials Credentials)?> ResolveAsync(
        int tenantId,
        string currency,
        CancellationToken cancellationToken)
    {
        var account = await _accounts.FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);

        if (account is not null)
        {
            var provider = _providers.Find(account.Provider);

            return provider is not null && Supports(provider, currency)
                ? (provider, Decrypt(account))
                : null;
        }

        if (_options.AllowSimulated && _providers.Find(PaymentProviderIds.Simulated) is { } simulated)
        {
            return (simulated, new PaymentAccountCredentials("", "", null, Live: false));
        }

        return null;
    }

    private async Task<(IPaymentProvider Provider, PaymentAccountCredentials Credentials)?> LoadAccountAsync(
        int tenantId,
        string providerId,
        CancellationToken cancellationToken)
    {
        var account = await _accounts.FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);
        var provider = _providers.Find(providerId);

        return account is not null && provider is not null &&
               string.Equals(account.Provider, provider.Descriptor.Id, StringComparison.OrdinalIgnoreCase)
            ? (provider, Decrypt(account))
            : null;
    }

    public static bool Supports(IPaymentProvider provider, string currency) =>
        provider.Descriptor.Currencies.Contains("*") ||
        provider.Descriptor.Currencies.Contains(currency, StringComparer.OrdinalIgnoreCase);

    private PaymentAccountCredentials Decrypt(GymPaymentAccount account) =>
        new(
            _protector.Unprotect(account.ApiKeyEncrypted),
            _protector.Unprotect(account.SecretEncrypted),
            account.WebhookSecretEncrypted is null ? null : _protector.Unprotect(account.WebhookSecretEncrypted),
            account.IsLive);
}
