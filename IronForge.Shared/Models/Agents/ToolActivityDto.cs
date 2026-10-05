using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Shared.Models.Agents
{
    public class ToolActivityDto
    {
        public string ToolCallId { get; set; } = "";

        public string ToolName { get; set; } = "";

        public string? ArgumentsJson { get; set; }

        public string? ResultJson { get; set; }

        public ToolActivityStatus Status { get; set; }

        public DateTime? CreatedAtUtc { get; set; }
    }
}
