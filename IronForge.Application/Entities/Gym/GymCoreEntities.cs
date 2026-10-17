using IronForge.Shared.Models.Gym;

namespace IronForge.Application.Entities
{
    /// <summary>
    /// Gym-specific settings of a tenant (1:1). Like <see cref="Tenant"/> it
    /// has no tenant query filter: it is looked up by join code before a
    /// tenant is selected and by the billing job for every tenant, so callers
    /// always filter by TenantId explicitly.
    /// </summary>
    public class GymSettings
    {
        public int Id { get; set; }

        public int TenantId { get; set; }

        public string? Address { get; set; }

        public string? Phone { get; set; }

        public string? ContactEmail { get; set; }

        /// <summary>ISO 4217 code; every price and invoice of the gym uses it.</summary>
        public string Currency { get; set; } = "USD";

        public string TimeZoneId { get; set; } = "UTC";

        /// <summary>Unique across the platform; upper-case letters and digits.</summary>
        public string JoinCode { get; set; } = "";

        public int InvoiceLeadDays { get; set; } = 7;

        public int ReminderDaysBeforeDue { get; set; } = 3;

        public List<int> ReminderDaysAfterDue { get; set; } = [1, 7];

        public int GracePeriodDays { get; set; } = 14;

        /// <summary>Stripe Connect (Standard) account that receives member payments.</summary>
        public string? StripeConnectedAccountId { get; set; }

        public bool StripeChargesEnabled { get; set; }

        /// <summary>
        /// Last invoice number issued. Only incremented atomically in the
        /// database (IInvoiceNumberGenerator), never through the entity.
        /// </summary>
        public int LastInvoiceNumber { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime UpdatedAtUtc { get; set; }
    }

    /// <summary>A gym's own membership offer (not the platform subscription plan).</summary>
    public class MembershipPlan
    {
        public int Id { get; set; }

        public int TenantId { get; set; }

        public string Name { get; set; } = "";

        public string? Description { get; set; }

        public decimal Price { get; set; }

        public BillingInterval BillingInterval { get; set; }

        /// <summary>Archived plans keep existing members but cannot be chosen.</summary>
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAtUtc { get; set; }
    }

    /// <summary>
    /// A gym member. Staff can add members who have no app account yet
    /// (<see cref="UserId"/> is null); the profile is linked when the member
    /// accepts the invitation, matched by email.
    /// </summary>
    public class MemberProfile
    {
        public int Id { get; set; }

        public int TenantId { get; set; }

        public int? UserId { get; set; }

        public string FullName { get; set; } = "";

        /// <summary>Lower-case; unique per gym.</summary>
        public string Email { get; set; } = "";

        public string? Phone { get; set; }

        public DateOnly? DateOfBirth { get; set; }

        public string? Gender { get; set; }

        public decimal? HeightCm { get; set; }

        public string? EmergencyContact { get; set; }

        public string? Notes { get; set; }

        public int? MembershipPlanId { get; set; }

        public MembershipPlan? MembershipPlan { get; set; }

        public MemberStatus Status { get; set; } = MemberStatus.Pending;

        public DateTime JoinedAtUtc { get; set; }

        public DateTime? ApprovedAtUtc { get; set; }

        /// <summary>Last day covered by a paid invoice.</summary>
        public DateOnly? PaidThrough { get; set; }

        /// <summary>Start of the next billing period not yet invoiced.</summary>
        public DateOnly? NextBillingDate { get; set; }

        public DateOnly? FrozenUntil { get; set; }

        public DateTime? CancelledAtUtc { get; set; }

        /// <summary>Set when personal data was erased on request; invoices are kept.</summary>
        public DateTime? AnonymizedAtUtc { get; set; }

        public List<Invoice> Invoices { get; set; } = [];

        public List<CheckIn> CheckIns { get; set; } = [];
    }

    public class CheckIn
    {
        public long Id { get; set; }

        public int TenantId { get; set; }

        public int MemberProfileId { get; set; }

        public MemberProfile? MemberProfile { get; set; }

        public DateTime CheckedInAtUtc { get; set; }

        /// <summary>Staff member at the front desk; null for a self check-in.</summary>
        public int? RecordedByUserId { get; set; }
    }

    public class PasswordResetToken
    {
        public Guid Id { get; set; }

        public int UserId { get; set; }

        /// <summary>SHA-256 of the emailed token; the token itself is never stored.</summary>
        public string TokenHash { get; set; } = "";

        public DateTime CreatedAtUtc { get; set; }

        public DateTime ExpiresAtUtc { get; set; }

        public DateTime? UsedAtUtc { get; set; }
    }
}
