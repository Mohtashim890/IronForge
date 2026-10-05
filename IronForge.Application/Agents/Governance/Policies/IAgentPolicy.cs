using IronForge.Application.Agents.Governance;
using IronForge.Application.Agents.Models;

namespace IronForge.Application.Agents.Governance.Policies
{
    public interface IAgentPolicy
    {
        AgentGovernanceDecision? Evaluate(
       AgentIdentity agent,
       AgentToolPolicy toolPolicy,
       AgentGovernanceContext context);
    }
}
