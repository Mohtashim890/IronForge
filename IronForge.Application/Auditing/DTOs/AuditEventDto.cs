using System.Text.Json.Serialization;
using IronForge.Application.Auditing.Models;

namespace IronForge.Application.Auditing.DTOs
{
    public sealed class AuditEventDto
    {
        public long Id { get; set; }
        public DateTime OccurredAtUtc { get; set; }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public AuditCategory Category { get; set; }
        public string Action { get; set; } = "";
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public AuditOutcome Outcome { get; set; }

        [JsonConverter(typeof(JsonStringEnumConverter))]
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
    }
}
