using IronForge.Application.Agents.Governance;

namespace IronForge.Application.Agents.Approvals;

/// <summary>
/// Four-eyes rule. Low/Medium-risk actions are approved by the requester in
/// their chat. High/Critical-risk actions must be approved by a different
/// user holding agents.approvals.decide (the Approvals queue); the requester
/// then runs the approved action under their own authority.
/// </summary>
public static class ApprovalRules
{
    public static bool RequiresSecondApprover(AgentRiskLevel riskLevel) =>
        riskLevel >= AgentRiskLevel.High;

    /// <summary>Whether a second approver has approved the request.</summary>
    public static bool HasSecondApproval(Entities.AgentApproval approval) =>
        approval.Status == AgentApprovalStatus.Approved &&
        approval.DecidedByUserId is { } decidedBy &&
        decidedBy != approval.UserId;
}
