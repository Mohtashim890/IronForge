using IronForge.Application.Agents.Models;

namespace IronForge.Application.Agents.Governance
{
    public interface IAgentGovernanceService
    {
        AgentGovernanceDecision Evaluate(
       AgentIdentity agent,
       AgentToolPolicy policy,
       AgentGovernanceContext context);
    }
}
