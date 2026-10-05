using IronForge.Application.Agents.Authorization;
using IronForge.Application.Agents.Governance;
using IronForge.Application.Agents.Models;

namespace IronForge.Application.Agents.Governance.Policies;

public class AgentPermissionPolicy : IAgentPolicy
{
    private readonly IAgentAuthorizationService _agentAuthorizationService;
    public AgentPermissionPolicy(IAgentAuthorizationService agentAuthorizationService)
    {
        _agentAuthorizationService = agentAuthorizationService;
    }
    public AgentGovernanceDecision? Evaluate(
        AgentIdentity agent,
        AgentToolPolicy toolPolicy,
        AgentGovernanceContext context)
    {
        var allowed = _agentAuthorizationService
            .HasPermissionAsync(agent, toolPolicy.RequiredPermission, default)
            .GetAwaiter()
            .GetResult();

        if (!allowed)
        {
            return new AgentGovernanceDecision
            {
                Decision =
                    AgentGovernanceDecisionType.Deny,

                Reason =
                    $"Agent '{agent.AgentId}' does not have " +
                    $"permission '{toolPolicy.RequiredPermission}'.",

                RiskLevel =
                    toolPolicy.RiskLevel,

                RequiresApproval =
                    toolPolicy.RequiresApproval
            };
        }

        return null;
    }
}