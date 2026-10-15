using IronForge.Application.Agents.Models;

namespace IronForge.Application.Agents.Governance
{
    public interface IAgentGovernanceService
    {
        Task<AgentGovernanceDecision> EvaluateAsync(
            AgentIdentity agent,
            AgentToolPolicy policy,
            AgentGovernanceContext context,
            CancellationToken cancellationToken = default);
    }
}
