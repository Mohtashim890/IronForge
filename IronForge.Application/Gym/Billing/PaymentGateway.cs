using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Commons;
using IronForge.Application.Entities;
using IronForge.Application.Persistence;
using IronForge.Shared.Models.Gym;
using Microsoft.Extensions.Logging;

namespace IronForge.Application.Gym.Billing;

public sealed class CheckoutRequest
{
    public required int TenantId { get; init; }

    public required int InvoiceId { get; init; }

    public required string InvoiceNumber { get; init; }

    public required string Description { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    public string? CustomerEmail { get; init; }

    /// <summary>The gym's Stripe Connect account; null charges the platform account.</summary>
    public string? ConnectedAccountId { get; init; }
}

public sealed record CheckoutResult(string SessionId, string Url);

public sealed record ConnectOnboardingLink(string AccountId, string Url);

/// <summary>
/// Card payments for member invoices. Stripe Checkout in production; a
/// simulated gateway when no Stripe keys are configured (local and demo).
/// </summary>
public interface IPaymentGateway
{
    /// <summary>"stripe" or "simulated"; stored on payments.</summary>
    string Provider { get; }

    bool IsSimulated { get; }

    Task<CheckoutResult> CreateCheckoutAsync(
        CheckoutRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Creates the gym's Connect account when needed and returns its onboarding link.</summary>
    Task<ConnectOnboardingLink> CreateConnectOnboardingLinkAsync(
        string? existingAccountId,
        string? email,
        CancellationToken cancellationToken = default);

    Task<bool> IsChargesEnabledAsync(
        string accountId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Applies a completed card checkout. Called by the Stripe webhook and the
/// simulated gateway, without a user: the caller establishes the tenant
/// execution context from the checkout's metadata first.
/// </summary>
public interface IPaymentConfirmationService
{
    Task<ServiceResult<bool>> ConfirmCheckoutAsync(
        string eventId,
        int tenantId,
        int invoiceId,
        string sessionId,
        decimal amountPaid,
        string currency,
        CancellationToken cancellationToken = default);
}

public sealed class PaymentConfirmationService : IPaymentConfirmationService
{
    private readonly IRepository<ProcessedWebhookEvent> _processed;
    private readonly IRepository<Invoice> _invoices;
    private readonly IRepository<MemberProfile> _members;
    private readonly IBillingEngine _billing;
    private readonly IPaymentGateway _gateway;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;
    private readonly ILogger<PaymentConfirmationService> _logger;

    public PaymentConfirmationService(
        IRepository<ProcessedWebhookEvent> processed,
        IRepository<Invoice> invoices,
        IRepository<MemberProfile> members,
        IBillingEngine billing,
        IPaymentGateway gateway,
        IUnitOfWork unitOfWork,
        IAuditService audit,
        TimeProvider clock,
        ILogger<PaymentConfirmationService> logger)
    {
        _processed = processed;
        _invoices = invoices;
        _members = members;
        _billing = billing;
        _gateway = gateway;
        _unitOfWork = unitOfWork;
        _audit = audit;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ServiceResult<bool>> ConfirmCheckoutAsync(
        string eventId,
        int tenantId,
        int invoiceId,
        string sessionId,
        decimal amountPaid,
        string currency,
        CancellationToken cancellationToken = default)
    {
        if (await _processed.AnyAsync(x => x.Id == eventId, cancellationToken))
        {
            return ServiceResult<bool>.Ok(false);
        }

        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(
                () => ConfirmInTransactionAsync(
                    eventId, tenantId, invoiceId, sessionId, amountPaid, currency, cancellationToken),
                cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // Paid at the same moment by another path (e.g. cash at the desk).
            _logger.LogWarning(
                "Payment event {EventId} for invoice {InvoiceId} lost a concurrent update.",
                eventId,
                invoiceId);

            return ServiceResult<bool>.Fail("The invoice was updated concurrently.", 409);
        }
    }

    private async Task<ServiceResult<bool>> ConfirmInTransactionAsync(
        string eventId,
        int tenantId,
        int invoiceId,
        string sessionId,
        decimal amountPaid,
        string currency,
        CancellationToken cancellationToken)
    {
        _processed.Add(new ProcessedWebhookEvent
        {
            Id = eventId,
            Provider = _gateway.Provider,
            ProcessedAtUtc = _clock.GetUtcNow().UtcDateTime
        });

        var invoice = await _invoices.FirstOrDefaultTrackedAsync(
            x => x.Id == invoiceId && x.TenantId == tenantId,
            cancellationToken);

        if (invoice is null)
        {
            await _processed.SaveChangesAsync(cancellationToken);
            return ServiceResult<bool>.Fail("Invoice was not found.", 404);
        }

        if (!invoice.IsUnpaid)
        {
            // Already paid (e.g. at the desk) or voided: record the event, change nothing.
            await _processed.SaveChangesAsync(cancellationToken);

            await _audit.RecordAsync(
                AuditCategory.Billing,
                "CardPaymentIgnored",
                AuditOutcome.Failed,
                AuditActorType.System,
                "Invoice",
                invoice.Id.ToString(),
                reason: $"Invoice was {invoice.Status} when checkout {sessionId} completed; refund it in the payment provider.",
                cancellationToken: cancellationToken,
                tenantId: tenantId);

            return ServiceResult<bool>.Ok(false);
        }

        if (amountPaid != invoice.Amount ||
            !string.Equals(currency, invoice.Currency, StringComparison.OrdinalIgnoreCase))
        {
            await _processed.SaveChangesAsync(cancellationToken);

            await _audit.RecordAsync(
                AuditCategory.Billing,
                "CardPaymentMismatch",
                AuditOutcome.Failed,
                AuditActorType.System,
                "Invoice",
                invoice.Id.ToString(),
                reason: $"Paid {amountPaid} {currency}, invoice is {invoice.Amount} {invoice.Currency}.",
                cancellationToken: cancellationToken,
                tenantId: tenantId);

            return ServiceResult<bool>.Fail("Paid amount does not match the invoice.", 400);
        }

        var member = await _members.FirstOrDefaultTrackedAsync(
            x => x.Id == invoice.MemberProfileId,
            cancellationToken);

        var gym = await _billing.GetGymAsync(tenantId, cancellationToken);

        if (member is null || gym is null)
        {
            return ServiceResult<bool>.Fail("Member or gym was not found.", 404);
        }

        _billing.ApplyPayment(
            gym,
            invoice,
            member,
            PaymentMethod.Card,
            _gateway.Provider,
            sessionId,
            recordedByUserId: null);

        await _invoices.SaveChangesAsync(cancellationToken);

        await _audit.RecordAsync(
            AuditCategory.Billing,
            "CardPaymentReceived",
            AuditOutcome.Succeeded,
            AuditActorType.System,
            "Invoice",
            invoice.Id.ToString(),
            reason: $"{invoice.Number} paid by card ({_gateway.Provider}).",
            cancellationToken: cancellationToken,
            tenantId: tenantId);

        return ServiceResult<bool>.Ok(true);
    }
}
