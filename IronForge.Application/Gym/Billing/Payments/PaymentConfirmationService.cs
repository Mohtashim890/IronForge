using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Commons;
using IronForge.Application.Entities;
using IronForge.Application.Persistence;
using IronForge.Shared.Models.Gym;
using Microsoft.Extensions.Logging;

namespace IronForge.Application.Gym.Billing.Payments;

/// <summary>
/// Applies a completed online payment. Called without a user (webhook,
/// return page, simulated checkout) or for the member; the caller has set
/// the tenant execution context and checked the outcome with the provider.
/// </summary>
public interface IPaymentConfirmationService
{
    /// <summary>
    /// Idempotent per <paramref name="eventId"/>: true when the payment was
    /// applied now, false when it was already handled or the invoice no
    /// longer needs paying.
    /// </summary>
    Task<ServiceResult<bool>> ConfirmCheckoutAsync(
        string provider,
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
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;
    private readonly ILogger<PaymentConfirmationService> _logger;

    public PaymentConfirmationService(
        IRepository<ProcessedWebhookEvent> processed,
        IRepository<Invoice> invoices,
        IRepository<MemberProfile> members,
        IBillingEngine billing,
        IUnitOfWork unitOfWork,
        IAuditService audit,
        TimeProvider clock,
        ILogger<PaymentConfirmationService> logger)
    {
        _processed = processed;
        _invoices = invoices;
        _members = members;
        _billing = billing;
        _unitOfWork = unitOfWork;
        _audit = audit;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ServiceResult<bool>> ConfirmCheckoutAsync(
        string provider,
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
                    provider, eventId, tenantId, invoiceId, sessionId, amountPaid, currency, cancellationToken),
                cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // Paid at the same moment by another path (another webhook, cash at the desk).
            _logger.LogWarning(
                "Payment event {EventId} for invoice {InvoiceId} lost a concurrent update.",
                eventId,
                invoiceId);

            return ServiceResult<bool>.Fail("The invoice was updated concurrently.", 409);
        }
    }

    private async Task<ServiceResult<bool>> ConfirmInTransactionAsync(
        string provider,
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
            Provider = provider,
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
                "OnlinePaymentIgnored",
                AuditOutcome.Failed,
                AuditActorType.System,
                "Invoice",
                invoice.Id.ToString(),
                reason: $"Invoice was {invoice.Status} when {provider} checkout {sessionId} completed; refund it in the provider's dashboard.",
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
                "OnlinePaymentMismatch",
                AuditOutcome.Failed,
                AuditActorType.System,
                "Invoice",
                invoice.Id.ToString(),
                reason: $"Paid {amountPaid} {currency} through {provider}, invoice is {invoice.Amount} {invoice.Currency}.",
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
            PaymentMethod.Online,
            provider,
            sessionId,
            recordedByUserId: null);

        await _invoices.SaveChangesAsync(cancellationToken);

        await _audit.RecordAsync(
            AuditCategory.Billing,
            "OnlinePaymentReceived",
            AuditOutcome.Succeeded,
            AuditActorType.System,
            "Invoice",
            invoice.Id.ToString(),
            reason: $"{invoice.Number} paid online ({provider}).",
            cancellationToken: cancellationToken,
            tenantId: tenantId);

        return ServiceResult<bool>.Ok(true);
    }
}
