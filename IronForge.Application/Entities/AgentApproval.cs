using IronForge.Application.Agents.Approvals;
using IronForge.Application.Agents.Governance;

namespace IronForge.Application.Entities;
public class AgentApproval
{
    public string ApprovalId { get; set; } = "";

    public string RequestId { get; set; } = "";

    public Guid SessionId { get; set; }

    public int TenantId { get; set; }

    public int UserId { get; set; }

    public string AgentId { get; set; } = "";

    public string ToolName { get; set; } = "";

    public string ToolCallId { get; set; } = "";

    public string ArgumentsJson { get; set; } = "";

    public AgentRiskLevel RiskLevel { get; set; }

    public AgentApprovalStatus Status { get; set; }

    public string? DecisionReason { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? DecidedAtUtc { get; set; }

    public DateTime? ExecutedAtUtc { get; set; }
}
