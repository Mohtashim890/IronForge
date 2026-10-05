using IronForge.Application.Auditing.Models;

namespace IronForge.Application.Entities
{
    public sealed class AuditEvent
    {
        public long Id { get; set; }

        public int? TenantId { get; set; }

        public DateTime OccurredAtUtc { get; set; }

        public AuditCategory Category { get; set; }

        public string Action { get; set; } = "";

        public AuditOutcome Outcome { get; set; }

        public AuditActorType ActorType { get; set; }

        public int? UserId { get; set; }

        public string? AgentId { get; set; }

        public string? ClientId { get; set; }

        public Guid? DelegationId { get; set; }

        public string? ResourceType { get; set; }

        public string? ResourceId { get; set; }

        public string? CorrelationId { get; set; }

        public string? TraceId { get; set; }

        public string? Reason { get; set; }

        public string? MetadataJson { get; set; }
    }
}
