using IronForge.Application.Agents.Memory.AgentMemory;

namespace IronForge.Application.Entities
{
    public class AgentMemory
    {
        public long Id { get; set; }

        public int TenantId { get; set; }

        public int? UserId { get; set; }

        public string? AgentId { get; set; }

        public Guid? SessionId { get; set; }

        public MemoryScope Scope { get; set; }

        public MemoryType Type { get; set; }

        public string Content { get; set; } = "";

        public MemorySource Source { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime UpdatedAtUtc { get; set; }

        public DateTime? ExpiresAtUtc { get; set; }
    }
}
