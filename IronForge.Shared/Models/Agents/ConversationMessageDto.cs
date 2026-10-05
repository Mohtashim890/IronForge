using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Shared.Models.Agents
{
    public class ConversationMessageDto
    {
        public long Id { get; set; }

        public Guid SessionId { get; set; }

        public ConversationMessageRole Role { get; set; }

        public string Content { get; set; } = "";

        public string? ToolCallId { get; set; }

        public string? ToolName { get; set; }

        public string? MetadataJson { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }
}
