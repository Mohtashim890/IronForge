
namespace IronForge.Application.Agents.Models
{
    public class AgentRunResult
    {
        public Guid SessionId { get; set; }
        public AgentRunStatus Status { get; set; }

        public string? Message { get; set; }

        public List<AgentApprovalRequest> Approvals { get; set; }
            = [];
    }
    public enum AgentRunStatus
    {
        Completed,
        ApprovalRequired
    }
    public class AgentApprovalRequest
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
