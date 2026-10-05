using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Shared.Models.Agents
{
    public class AgentApprovalDecisionResultDto
    {
        public Guid SessionId { get; set; }

        public bool Approved { get; set; }

        public bool Executed { get; set; }

        public string? Message { get; set; }
    }
}
