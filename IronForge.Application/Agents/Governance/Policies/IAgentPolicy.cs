using IronForge.Application.Agents.Models;

namespace IronForge.Application.Agents.Governance.Policies
{
    /// <summary>
    /// One governance rule. Returns null when the rule has no opinion,
    /// otherwise a Deny or ApprovalRequired decision.
    /// </summary>
    public interface IAgentPolicy
    {
        ValueTask<AgentGovernanceDecision?> EvaluateAsync(
            AgentIdentity agent,
            AgentToolPolicy toolPolicy,
            AgentGovernanceContext context,
            CancellationToken cancellationToken = default);
    }
}
