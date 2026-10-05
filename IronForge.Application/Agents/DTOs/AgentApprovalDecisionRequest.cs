using IronForge.Application.Agents.Approvals;

namespace IronForge.Application.Agents.DTOs
{
    public class AgentApprovalDecisionRequest
    {
        public bool Approved { get; set; }
        public AgentApprovalStatus Status { get; set; }

        public string? Reason { get; set; }
    }
}
