using System.ComponentModel.DataAnnotations;

namespace IronForge.Shared.Models.Gym
{
    public sealed class GymSettingsDto
    {
        public int TenantId { get; set; }

        public string GymName { get; set; } = "";

        public string? Address { get; set; }

        public string? Phone { get; set; }

        public string? ContactEmail { get; set; }

        /// <summary>ISO 4217 code, e.g. USD. One currency per gym.</summary>
        public string Currency { get; set; } = "USD";

        /// <summary>IANA or Windows time zone id used to show dates.</summary>
        public string TimeZoneId { get; set; } = "UTC";

        /// <summary>Members enter this code in the app to request to join.</summary>
        public string JoinCode { get; set; } = "";

        /// <summary>Invoices are created this many days before the billing date.</summary>
        public int InvoiceLeadDays { get; set; }

        public int ReminderDaysBeforeDue { get; set; }

        public List<int> ReminderDaysAfterDue { get; set; } = [];

        /// <summary>Days an invoice may stay overdue before the membership expires.</summary>
        public int GracePeriodDays { get; set; }

        public bool StripeConnected { get; set; }

        public string? StripeConnectedAccountId { get; set; }

        /// <summary>True when card payments go through a simulated gateway (no Stripe keys).</summary>
        public bool PaymentsSimulated { get; set; }

        /// <summary>Stripe is configured on the platform, so the gym can connect its account.</summary>
        public bool StripeAvailable { get; set; }

        /// <summary>Members of this gym can pay invoices online in the app now.</summary>
        public bool OnlinePaymentsAvailable { get; set; }

        /// <summary>How members pay at the desk or by bank transfer; shown in the app.</summary>
        public string? PaymentInstructions { get; set; }

        /// <summary>Members show their rotating pass; staff or a kiosk scan it.</summary>
        public bool MemberPassCheckInEnabled { get; set; }

        /// <summary>A check-in device shows a rotating code; members scan it with the app.</summary>
        public bool GymQrCheckInEnabled { get; set; }

        /// <summary>The app asks for the phone's fingerprint, face or screen lock before a pass or a scan.</summary>
        public bool RequirePhoneUnlock { get; set; }

        /// <summary>The Access control module (fingerprint terminals) is part of the gym's subscription.</summary>
        public bool AccessControlEnabled { get; set; }
    }

    /// <summary>
    /// Which self-service check-in methods the gym offers. Staff can always
    /// check a member in by name at the front desk.
    /// </summary>
    public sealed class UpdateCheckInMethodsRequest
    {
        public bool MemberPassEnabled { get; set; }

        public bool GymQrEnabled { get; set; }

        public bool RequirePhoneUnlock { get; set; } = true;
    }

    public sealed class UpdateGymSettingsRequest
    {
        [MaxLength(300)]
        public string? Address { get; set; }

        [MaxLength(50)]
        public string? Phone { get; set; }

        [EmailAddress]
        [MaxLength(256)]
        public string? ContactEmail { get; set; }

        [Required]
        [StringLength(3, MinimumLength = 3)]
        public string Currency { get; set; } = "USD";

        [Required]
        [MaxLength(100)]
        public string TimeZoneId { get; set; } = "UTC";

        [Range(0, 30)]
        public int InvoiceLeadDays { get; set; } = 7;

        [Range(0, 30)]
        public int ReminderDaysBeforeDue { get; set; } = 3;

        public List<int> ReminderDaysAfterDue { get; set; } = [1, 7];

        [Range(0, 90)]
        public int GracePeriodDays { get; set; } = 14;

        [MaxLength(1000)]
        public string? PaymentInstructions { get; set; }
    }

    public sealed class StripeConnectLinkDto
    {
        public string Url { get; set; } = "";
    }

    /// <summary>Public information shown before joining with a code.</summary>
    public sealed class GymJoinPreviewDto
    {
        public string GymName { get; set; } = "";

        public string? Address { get; set; }

        public string Currency { get; set; } = "USD";

        public List<MembershipPlanDto> Plans { get; set; } = [];
    }

    public sealed class GymJoinPreviewRequest
    {
        [Required]
        [MaxLength(20)]
        public string JoinCode { get; set; } = "";
    }

    public sealed class GymJoinRequest
    {
        [Required]
        [MaxLength(20)]
        public string JoinCode { get; set; } = "";

        [Required]
        [MaxLength(150)]
        public string FullName { get; set; } = "";

        [Required]
        [EmailAddress]
        [MaxLength(256)]
        public string Email { get; set; } = "";

        [Required]
        [StringLength(50, MinimumLength = 3)]
        public string Username { get; set; } = "";

        [Required]
        [StringLength(128, MinimumLength = 8)]
        public string Password { get; set; } = "";

        [MaxLength(50)]
        public string? Phone { get; set; }

        public int? MembershipPlanId { get; set; }
    }

    public sealed class GymJoinResultDto
    {
        public string GymName { get; set; } = "";

        public string Username { get; set; } = "";

        public bool AccountCreated { get; set; }

        public MemberStatus Status { get; set; }

        /// <summary>
        /// The gym had already added this person. No request was made: an
        /// invitation to set up their sign-in was emailed to them instead.
        /// </summary>
        public bool InvitationEmailed { get; set; }

        /// <summary>The account was linked to the person's existing membership and can sign in now.</summary>
        public bool AccessGranted { get; set; }
    }
}
