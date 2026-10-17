using IronForge.Shared.Models.Gym;

namespace IronForge.Application.Entities
{
    public class Invoice
    {
        public int Id { get; set; }

        public int TenantId { get; set; }

        /// <summary>Per-gym sequence, e.g. INV-000042.</summary>
        public string Number { get; set; } = "";

        public int MemberProfileId { get; set; }

        public MemberProfile? MemberProfile { get; set; }

        public int? MembershipPlanId { get; set; }

        public string Description { get; set; } = "";

        public DateOnly PeriodStart { get; set; }

        public DateOnly PeriodEnd { get; set; }

        public decimal Amount { get; set; }

        public string Currency { get; set; } = "USD";

        public DateTime IssuedAtUtc { get; set; }

        public DateOnly DueDate { get; set; }

        public InvoiceStatus Status { get; set; } = InvoiceStatus.Open;

        public DateTime? PaidAtUtc { get; set; }

        public DateTime? VoidedAtUtc { get; set; }

        /// <summary>Latest payment-provider checkout started for this invoice.</summary>
        public string? CheckoutSessionId { get; set; }

        /// <summary>Concurrency token (Postgres xmin): a webhook and a cash payment cannot both pay it.</summary>
        public uint Version { get; set; }

        public bool IsUnpaid => Status is InvoiceStatus.Open or InvoiceStatus.Overdue;
    }

    public class Payment
    {
        public int Id { get; set; }

        public int TenantId { get; set; }

        public int InvoiceId { get; set; }

        public Invoice? Invoice { get; set; }

        public int MemberProfileId { get; set; }

        public decimal Amount { get; set; }

        public string Currency { get; set; } = "USD";

        public PaymentMethod Method { get; set; }

        /// <summary>"manual", "stripe" or "simulated".</summary>
        public string Provider { get; set; } = "";

        public string? ProviderReference { get; set; }

        public int? RecordedByUserId { get; set; }

        public DateTime PaidAtUtc { get; set; }
    }

    /// <summary>
    /// A payment reminder sent for an invoice. Automatic reminders are unique
    /// per (invoice, kind), so the billing job never sends one twice.
    /// </summary>
    public class InvoiceReminder
    {
        public int Id { get; set; }

        public int TenantId { get; set; }

        public int InvoiceId { get; set; }

        public Invoice? Invoice { get; set; }

        /// <summary>"before:3", "after:7", or "manual:{guid}".</summary>
        public string Kind { get; set; } = "";

        public DateTime SentAtUtc { get; set; }
    }

    /// <summary>Payment-provider events already handled (idempotency).</summary>
    public class ProcessedWebhookEvent
    {
        public string Id { get; set; } = "";

        public string Provider { get; set; } = "";

        public DateTime ProcessedAtUtc { get; set; }
    }
}
