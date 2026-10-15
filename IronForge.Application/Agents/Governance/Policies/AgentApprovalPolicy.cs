using IronForge.Application.Agents.Governance;
using IronForge.Application.Agents.Models;

namespace IronForge.Application.Agents.Governance.Policies;

public class AgentApprovalPolicy : IAgentPolicy
{
    public ValueTask<AgentGovernanceDecision?> EvaluateAsync(
        AgentIdentity agent,
        AgentToolPolicy toolPolicy,
        AgentGovernanceContext context,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(Evaluate(toolPolicy, context));
    }

    private static AgentGovernanceDecision? Evaluate(
        AgentToolPolicy toolPolicy,
        AgentGovernanceContext context)
    {
        if (!toolPolicy.RequiresApproval)
        {
            return null;
        }

        return new AgentGovernanceDecision
        {
            Decision =
                AgentGovernanceDecisionType.ApprovalRequired,

            Reason =
                $"Tool '{toolPolicy.ToolName}' requires human approval.",

            RiskLevel =
                toolPolicy.RiskLevel,

            RequiresApproval = true
        };
    }
}