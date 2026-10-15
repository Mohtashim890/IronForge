using IronForge.Application.Agents.Governance.Policies;
using IronForge.Application.Agents.Models;

namespace IronForge.Application.Agents.Governance;

public class AgentGovernanceService
    : IAgentGovernanceService
{
    private readonly IEnumerable<IAgentPolicy> _policies;

    public AgentGovernanceService(
        IEnumerable<IAgentPolicy> policies)
    {
        _policies = policies;
    }
    public async Task<AgentGovernanceDecision> EvaluateAsync(
        AgentIdentity agent,
        AgentToolPolicy toolPolicy,
        AgentGovernanceContext context,
        CancellationToken cancellationToken = default)
    {
        AgentGovernanceDecision? approvalDecision = null;

        foreach (var policy in _policies)
        {
            var decision =
                await policy.EvaluateAsync(
                    agent,
                    toolPolicy,
                    context,
                    cancellationToken);

            if (decision is null)
            {
                continue;
            }

            if (decision.Decision ==
                AgentGovernanceDecisionType.Deny)
            {
                return decision;
            }

            if (decision.Decision ==
                AgentGovernanceDecisionType.ApprovalRequired)
            {
                // Remember it, but continue evaluating.
                approvalDecision ??= decision;
            }
        }

        if (approvalDecision is not null)
        {
            return approvalDecision;
        }

        return new AgentGovernanceDecision
        {
            Decision =
                AgentGovernanceDecisionType.Allow,

            Reason =
                $"Tool '{toolPolicy.ToolName}' is allowed.",

            RiskLevel =
                toolPolicy.RiskLevel,

            RequiresApproval = false
        };
    }
}