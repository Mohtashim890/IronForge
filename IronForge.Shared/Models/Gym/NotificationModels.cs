using System.ComponentModel.DataAnnotations;

namespace IronForge.Shared.Models.Gym
{
    public sealed class NotificationDto
    {
        public long Id { get; set; }

        public NotificationType Type { get; set; }

        public string Title { get; set; } = "";

        public string Body { get; set; } = "";

        public string? Link { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public bool IsRead { get; set; }
    }

    public sealed class UnreadCountDto
    {
        public int Count { get; set; }
    }

    public sealed class AnnouncementDto
    {
        public int Id { get; set; }

        public string Title { get; set; } = "";

        public string Body { get; set; } = "";

        public AnnouncementAudience Audience { get; set; }

        public int? MembershipPlanId { get; set; }

        public string SentBy { get; set; } = "";

        public DateTime SentAtUtc { get; set; }

        public int RecipientCount { get; set; }
    }

    public sealed class CreateAnnouncementRequest
    {
        [Required]
        [MaxLength(150)]
        public string Title { get; set; } = "";

        [Required]
        [MaxLength(4000)]
        public string Body { get; set; } = "";

        public AnnouncementAudience Audience { get; set; }

        /// <summary>Required when <see cref="Audience"/> is Plan.</summary>
        public int? MembershipPlanId { get; set; }

        public bool SendEmail { get; set; } = true;
    }

    public sealed class ForgotPasswordRequest
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";
    }

    public sealed class ResetPasswordRequest
    {
        [Required]
        public string Token { get; set; } = "";

        [Required]
        [StringLength(128, MinimumLength = 8)]
        public string NewPassword { get; set; } = "";
    }
}
