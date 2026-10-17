using IronForge.Application.Agents.Models;
using IronForge.Application.Auth.Services;

namespace IronForge.Application.Agents.Governance.Policies;

/// <summary>
/// An agent never acts with more authority than the person it works for:
/// the user must hold the tool's permission, whatever the agent holds.
///
/// For internal agents these are the signed-in user's tenant-role
/// permissions. For external (MCP) agents they are the delegated scopes in
/// the credential, which were a subset of the user's permissions when issued.
/// Evaluated again at approved execution, so the approver needs it too.
/// </summary>
public sealed class UserPermissionPolicy : IAgentPolicy
{
    private readonly IUserAuthorizationService _userAuthorization;

    public UserPermissionPolicy(IUserAuthorizationService userAuthorization)
    {
        _userAuthorization = userAuthorization;
    }

    public ValueTask<AgentGovernanceDecision?> EvaluateAsync(
        AgentIdentity agent,
        AgentToolPolicy toolPolicy,
        AgentGovernanceContext context,
        CancellationToken cancellationToken = default)
    {
        if (_userAuthorization.HasPermission(toolPolicy.RequiredPermission))
        {
            return ValueTask.FromResult<AgentGovernanceDecision?>(null);
        }

        return ValueTask.FromResult<AgentGovernanceDecision?>(
            new AgentGovernanceDecision
            {
                Decision = AgentGovernanceDecisionType.Deny,
                Reason =
                    $"You do not have permission '{toolPolicy.RequiredPermission}', " +
                    $"so agent '{agent.AgentId}' cannot use it on your behalf.",
                RiskLevel = toolPolicy.RiskLevel,
                RequiresApproval = toolPolicy.RequiresApproval
            });
    }
}
