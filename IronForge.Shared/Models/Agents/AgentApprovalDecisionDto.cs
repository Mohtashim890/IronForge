using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Shared.Models.Agents
{
    public class AgentApprovalDecisionDto
    {
        public string ApprovalId { get; set; } = "";

        public bool Approved { get; set; }

        public string? Reason { get; set; }
    }
}
