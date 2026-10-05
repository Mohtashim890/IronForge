using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Shared.Models.Agents
{
    public class AgentApprovalRequestDto
    {
        public string ApprovalId { get; set; } = "";

        public string ToolName { get; set; } = "";

        public string ArgumentsJson { get; set; } = "";

        public string RiskLevel { get; set; } = "";

        public string? Description { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime ExpiresAtUtc { get; set; }
    }
}
