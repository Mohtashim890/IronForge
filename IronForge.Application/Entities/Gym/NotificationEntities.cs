using IronForge.Shared.Models.Gym;

namespace IronForge.Application.Entities
{
    /// <summary>An in-app notification for one user in one gym.</summary>
    public class Notification
    {
        public long Id { get; set; }

        public int TenantId { get; set; }

        public int UserId { get; set; }

        public NotificationType Type { get; set; }

        public string Title { get; set; } = "";

        public string Body { get; set; } = "";

        public string? Link { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime? ReadAtUtc { get; set; }
    }

    public class Announcement
    {
        public int Id { get; set; }

        public int TenantId { get; set; }

        public string Title { get; set; } = "";

        public string Body { get; set; } = "";

        public AnnouncementAudience Audience { get; set; }

        public int? MembershipPlanId { get; set; }

        public int SentByUserId { get; set; }

        public string SentByName { get; set; } = "";

        public DateTime SentAtUtc { get; set; }

        public int RecipientCount { get; set; }
    }

    public enum EmailStatus
    {
        Pending = 0,
        Sent = 1,
        Failed = 2
    }

    /// <summary>
    /// An email waiting to be sent. Written in the same transaction as the
    /// change that caused it and delivered by a background processor with
    /// retries, so a slow or failing mail server never fails a request.
    /// Not tenant-filtered: the processor serves every tenant.
    /// </summary>
    public class EmailOutboxMessage
    {
        public Guid Id { get; set; }

        public int? TenantId { get; set; }

        public string ToEmail { get; set; } = "";

        public string? ToName { get; set; }

        public string Subject { get; set; } = "";

        public string HtmlBody { get; set; } = "";

        public EmailStatus Status { get; set; } = EmailStatus.Pending;

        public int Attempts { get; set; }

        public DateTime NextAttemptAtUtc { get; set; }

        public string? LastError { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime? SentAtUtc { get; set; }
    }
}
