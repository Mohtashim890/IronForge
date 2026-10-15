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
    public async ValueTask<AgentGovernanceDecision?> EvaluateAsync(
        AgentIdentity agent,
        AgentToolPolicy toolPolicy,
        AgentGovernanceContext context,
        CancellationToken cancellationToken = default)
    {
        var allowed =
            await _agentAuthorizationService.HasPermissionAsync(
                agent,
                toolPolicy.RequiredPermission,
                cancellationToken);

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