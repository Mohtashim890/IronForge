using IronForge.Application.Auditing.Models;

namespace IronForge.Application.Auditing.DTOs
{
    public sealed class AuditQueryRequest
    {
        public AuditCategory? Category { get; set; }

        public AuditOutcome? Outcome { get; set; }

        public AuditActorType? ActorType { get; set; }

        public int? UserId { get; set; }

        public string? AgentId { get; set; }

        public Guid? DelegationId { get; set; }

        public string? ResourceType { get; set; }

        public string? ResourceId { get; set; }

        public string? Action { get; set; }

        public string? CorrelationId { get; set; }

        public string? TraceId { get; set; }

        public DateTime? FromUtc { get; set; }

        public DateTime? ToUtc { get; set; }

        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 50;
    }
}
