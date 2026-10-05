using IronForge.Application.Agents.Memory.ConversationSession;

namespace IronForge.Application.Entities
{
    public class ConversationMessage
    {
        public long Id { get; set; }

        public Guid SessionId { get; set; }

        public int TenantId { get; set; }

        public ConversationMessageRole Role { get; set; }

        public string Content { get; set; } = "";

        public string? ToolCallId { get; set; }

        public string? ToolName { get; set; }

        public string? MetadataJson { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }
}