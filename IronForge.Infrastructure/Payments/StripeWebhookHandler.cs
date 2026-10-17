using System.Globalization;
using IronForge.Application.Execution;
using IronForge.Application.Gym.Billing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace IronForge.Infrastructure.Payments;

public enum WebhookOutcome
{
    Handled,
    Ignored,
    InvalidSignature,
    NotConfigured
}

/// <summary>
/// Verifies and applies Stripe webhooks. Only checkout.session.completed
/// with payment_status=paid changes data; the tenant comes from the
/// session metadata this platform set when creating the checkout.
/// </summary>
public sealed class StripeWebhookHandler
{
    private readonly StripeOptions _options;
    private readonly IExecutionContextAccessor _executionContext;
    private readonly IPaymentConfirmationService _confirmation;
    private readonly ILogger<StripeWebhookHandler> _logger;

    public StripeWebhookHandler(
        IOptions<StripeOptions> options,
        IExecutionContextAccessor executionContext,
        IPaymentConfirmationService confirmation,
        ILogger<StripeWebhookHandler> logger)
    {
        _options = options.Value;
        _executionContext = executionContext;
        _confirmation = confirmation;
        _logger = logger;
    }

    public async Task<WebhookOutcome> HandleAsync(
        string payload,
        string? signature,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookSecret))
        {
            return WebhookOutcome.NotConfigured;
        }

        Event stripeEvent;

        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                payload,
                signature,
                _options.WebhookSecret,
                throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Rejected a Stripe webhook with an invalid signature.");
            return WebhookOutcome.InvalidSignature;
        }

        if (stripeEvent.Type != EventTypes.CheckoutSessionCompleted ||
            stripeEvent.Data.Object is not Session session ||
            session.PaymentStatus != "paid")
        {
            return WebhookOutcome.Ignored;
        }

        if (session.Metadata is null ||
            !session.Metadata.TryGetValue("tenant_id", out var tenantText) ||
            !session.Metadata.TryGetValue("invoice_id", out var invoiceText) ||
            !int.TryParse(tenantText, NumberStyles.None, CultureInfo.InvariantCulture, out var tenantId) ||
            !int.TryParse(invoiceText, NumberStyles.None, CultureInfo.InvariantCulture, out var invoiceId))
        {
            _logger.LogWarning("Stripe session {SessionId} has no IronForge invoice metadata.", session.Id);
            return WebhookOutcome.Ignored;
        }

        // Webhooks are anonymous: act as the system within the invoice's gym.
        _executionContext.Set(new IronForge.Application.Execution.ExecutionContext
        {
            TenantId = tenantId,
            Source = ExecutionSource.Background
        });

        var currency = (session.Currency ?? "").ToUpperInvariant();
        var amount = StripePaymentGateway.FromMinorUnits(session.AmountTotal ?? 0, currency);

        var result = await _confirmation.ConfirmCheckoutAsync(
            stripeEvent.Id,
            tenantId,
            invoiceId,
            session.Id,
            amount,
            currency,
            cancellationToken);

        if (!result.Success)
        {
            _logger.LogWarning(
                "Stripe event {EventId} for invoice {InvoiceId} was not applied: {Error}",
                stripeEvent.Id,
                invoiceId,
                result.ErrorMessage);
        }

        return WebhookOutcome.Handled;
    }
}
