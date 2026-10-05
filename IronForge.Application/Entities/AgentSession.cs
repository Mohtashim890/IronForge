using IronForge.Application.Agents.Memory.ConversationSession;

namespace IronForge.Application.Entities
{
    public class AgentSession
    {
        public Guid Id { get; set; }

        public int TenantId { get; set; }

        public int UserId { get; set; }

        public string AgentId { get; set; } = "";

        public string? Title { get; set; }

        public AgentSessionStatus Status { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime UpdatedAtUtc { get; set; }

        public DateTime? LastMessageAtUtc { get; set; }
    }
}