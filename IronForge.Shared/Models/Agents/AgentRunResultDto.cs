using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Shared.Models.Agents
{
    public class AgentRunResultDto
    {
        public Guid SessionId { get; set; }

        public AgentRunStatus Status { get; set; }

        public string? Message { get; set; }

        public List<AgentApprovalRequestDto> Approvals { get; set; } = [];
    }
}
