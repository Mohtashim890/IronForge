using System.ComponentModel.DataAnnotations;

namespace IronForge.Shared.Models.Gym
{
    public sealed class InvoiceDto
    {
        public int Id { get; set; }

        public string Number { get; set; } = "";

        public int MemberId { get; set; }

        public string MemberName { get; set; } = "";

        public string Description { get; set; } = "";

        public DateOnly PeriodStart { get; set; }

        public DateOnly PeriodEnd { get; set; }

        public decimal Amount { get; set; }

        public string Currency { get; set; } = "USD";

        public DateOnly DueDate { get; set; }

        public InvoiceStatus Status { get; set; }

        public DateTime IssuedAtUtc { get; set; }

        public DateTime? PaidAtUtc { get; set; }

        public int DaysOverdue { get; set; }
    }

    public sealed class InvoiceQuery
    {
        public InvoiceStatus? Status { get; set; }

        public int? MemberId { get; set; }

        public DateOnly? DueFrom { get; set; }

        public DateOnly? DueTo { get; set; }

        public string? Search { get; set; }

        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 25;
    }

    public sealed class PaymentDto
    {
        public int Id { get; set; }

        public int InvoiceId { get; set; }

        public string InvoiceNumber { get; set; } = "";

        public int MemberId { get; set; }

        public string MemberName { get; set; } = "";

        public decimal Amount { get; set; }

        public string Currency { get; set; } = "USD";

        public PaymentMethod Method { get; set; }

        public string Provider { get; set; } = "";

        public DateTime PaidAtUtc { get; set; }
    }

    public sealed class RecordPaymentRequest
    {
        [Range(typeof(decimal), "0.01", "1000000")]
        public decimal Amount { get; set; }

        public PaymentMethod Method { get; set; } = PaymentMethod.Cash;

        [MaxLength(200)]
        public string? Reference { get; set; }
    }

    public sealed class CheckoutSessionDto
    {
        /// <summary>Page where the member pays (Stripe Checkout or the simulated gateway).</summary>
        public string Url { get; set; } = "";

        public bool Simulated { get; set; }
    }

    public sealed class SendReminderResultDto
    {
        public int InvoiceId { get; set; }

        public string MemberName { get; set; } = "";

        public bool EmailQueued { get; set; }
    }

    public sealed class RevenuePointDto
    {
        /// <summary>First day of the month.</summary>
        public DateOnly Month { get; set; }

        public decimal Collected { get; set; }
    }
}
