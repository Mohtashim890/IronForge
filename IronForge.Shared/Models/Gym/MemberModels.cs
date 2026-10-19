using System.ComponentModel.DataAnnotations;

namespace IronForge.Shared.Models.Gym
{
    public sealed class MembershipPlanDto
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public string? Description { get; set; }

        public decimal Price { get; set; }

        public string Currency { get; set; } = "USD";

        public BillingInterval BillingInterval { get; set; }

        public bool IsActive { get; set; }

        public int ActiveMemberCount { get; set; }
    }

    public sealed class SaveMembershipPlanRequest
    {
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = "";

        [MaxLength(1000)]
        public string? Description { get; set; }

        [Range(typeof(decimal), "0", "100000")]
        public decimal Price { get; set; }

        public BillingInterval BillingInterval { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public sealed class MemberSummaryDto
    {
        public int Id { get; set; }

        public int? UserId { get; set; }

        public string FullName { get; set; } = "";

        public string Email { get; set; } = "";

        public string? Phone { get; set; }

        public MemberStatus Status { get; set; }

        public int? MembershipPlanId { get; set; }

        public string? PlanName { get; set; }

        public DateOnly? PaidThrough { get; set; }

        public DateOnly? NextBillingDate { get; set; }

        public decimal OutstandingAmount { get; set; }

        public DateTime JoinedAtUtc { get; set; }

        public DateTime? LastCheckInUtc { get; set; }

        /// <summary>False until the member signs up in the app.</summary>
        public bool HasAppAccount { get; set; }

        public bool HasPhoto { get; set; }
    }

    public sealed class MemberDetailDto
    {
        public MemberSummaryDto Summary { get; set; } = new();

        public DateOnly? DateOfBirth { get; set; }

        public string? Gender { get; set; }

        public decimal? HeightCm { get; set; }

        public string? EmergencyContact { get; set; }

        public string? Notes { get; set; }

        public DateOnly? FrozenUntil { get; set; }

        public int CheckInsLast30Days { get; set; }

        public List<InvoiceDto> RecentInvoices { get; set; } = [];

        public List<CheckInDto> RecentCheckIns { get; set; } = [];

        public List<RoutineSummaryDto> Routines { get; set; } = [];

        public List<FitnessGoalDto> Goals { get; set; } = [];

        public MemberPhotoDto? Photo { get; set; }
    }

    public sealed class MemberQuery
    {
        public string? Search { get; set; }

        public MemberStatus? Status { get; set; }

        public int? MembershipPlanId { get; set; }

        /// <summary>Only members with an unpaid overdue invoice.</summary>
        public bool OverdueOnly { get; set; }

        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 25;
    }

    public sealed class PagedResult<T>
    {
        public List<T> Items { get; set; } = [];

        public int TotalCount { get; set; }

        public int Page { get; set; }

        public int PageSize { get; set; }
    }

    /// <summary>Staff adds a member; an invitation email lets them create an app account.</summary>
    public sealed class CreateMemberRequest
    {
        [Required]
        [MaxLength(150)]
        public string FullName { get; set; } = "";

        [Required]
        [EmailAddress]
        [MaxLength(256)]
        public string Email { get; set; } = "";

        [MaxLength(50)]
        public string? Phone { get; set; }

        [Required]
        public int MembershipPlanId { get; set; }

        /// <summary>First day of the first billing period; today when empty.</summary>
        public DateOnly? StartDate { get; set; }

        public bool SendAppInvitation { get; set; } = true;
    }

    public sealed class UpdateMemberRequest
    {
        [Required]
        [MaxLength(150)]
        public string FullName { get; set; } = "";

        [MaxLength(50)]
        public string? Phone { get; set; }

        public DateOnly? DateOfBirth { get; set; }

        [MaxLength(20)]
        public string? Gender { get; set; }

        [Range(typeof(decimal), "50", "260")]
        public decimal? HeightCm { get; set; }

        [MaxLength(200)]
        public string? EmergencyContact { get; set; }

        [MaxLength(2000)]
        public string? Notes { get; set; }
    }

    /// <summary>The signed-in member updating their own profile.</summary>
    public sealed class UpdateMyProfileRequest
    {
        [Required]
        [MaxLength(150)]
        public string FullName { get; set; } = "";

        [MaxLength(50)]
        public string? Phone { get; set; }

        public DateOnly? DateOfBirth { get; set; }

        [MaxLength(20)]
        public string? Gender { get; set; }

        [Range(typeof(decimal), "50", "260")]
        public decimal? HeightCm { get; set; }

        [MaxLength(200)]
        public string? EmergencyContact { get; set; }
    }

    public sealed class ApproveMemberRequest
    {
        /// <summary>Plan to bill; keeps the plan chosen at sign-up when empty.</summary>
        public int? MembershipPlanId { get; set; }
    }

    public sealed class FreezeMemberRequest
    {
        [Required]
        public DateOnly Until { get; set; }

        [MaxLength(500)]
        public string? Reason { get; set; }
    }

    public sealed class ChangeMemberPlanRequest
    {
        [Required]
        public int MembershipPlanId { get; set; }
    }

    public sealed class CheckInDto
    {
        public long Id { get; set; }

        public int MemberId { get; set; }

        public string MemberName { get; set; } = "";

        public DateTime CheckedInAtUtc { get; set; }

        /// <summary>Checked in at an unattended device, without staff.</summary>
        public bool SelfCheckIn { get; set; }

        public CheckInMethod Method { get; set; }

        /// <summary>The check-in device, for <see cref="CheckInMethod.Kiosk"/>.</summary>
        public string? DeviceName { get; set; }

        /// <summary>
        /// The person got in although IronForge would have refused them, e.g.
        /// a fingerprint terminal that had not received an update yet.
        /// </summary>
        public string? Warning { get; set; }
    }

    public sealed class CreateCheckInRequest
    {
        [Required]
        public int MemberId { get; set; }
    }

    /// <summary>What the signed-in member sees on the home screen.</summary>
    public sealed class MyMembershipDto
    {
        public int MemberId { get; set; }

        public string GymName { get; set; } = "";

        public string FullName { get; set; } = "";

        public string Email { get; set; } = "";

        public string? Phone { get; set; }

        public DateOnly? DateOfBirth { get; set; }

        public string? Gender { get; set; }

        public decimal? HeightCm { get; set; }

        public string? EmergencyContact { get; set; }

        public MemberStatus Status { get; set; }

        public MembershipPlanDto? Plan { get; set; }

        public DateOnly? PaidThrough { get; set; }

        public DateOnly? NextBillingDate { get; set; }

        public DateOnly? FrozenUntil { get; set; }

        public InvoiceDto? NextDueInvoice { get; set; }

        public decimal OutstandingAmount { get; set; }

        public string Currency { get; set; } = "USD";

        public int WorkoutsThisWeek { get; set; }

        /// <summary>Consecutive weeks (including this one) with at least one workout.</summary>
        public int WeeklyStreak { get; set; }

        public int CheckInsLast30Days { get; set; }

        /// <summary>Whether the gym's plan includes the AI coach and fitness tracking.</summary>
        public bool CoachingEnabled { get; set; }

        public bool HasPhoto { get; set; }

        /// <summary>The gym accepts the member's own pass (Pass tab).</summary>
        public bool MemberPassCheckInEnabled { get; set; }

        /// <summary>The gym shows an entrance code the member scans with the app.</summary>
        public bool GymQrCheckInEnabled { get; set; }

        /// <summary>Unlock the phone (fingerprint, face or screen lock) before showing the pass or scanning.</summary>
        public bool RequirePhoneUnlock { get; set; }
    }
}
