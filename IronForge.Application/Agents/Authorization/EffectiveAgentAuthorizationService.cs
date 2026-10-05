using IronForge.Application.Agents.Delegations;
using IronForge.Application.Auth.Services;
using IronForge.Application.Execution;

namespace IronForge.Application.Agents.Authorization;

public class EffectiveAgentAuthorizationService
    : IEffectiveAgentAuthorizationService
{
    private readonly IExecutionContextAccessor _executionContext;
    private readonly IUserAuthorizationService _userAuthorization;
    private readonly IAgentAuthorizationService _agentAuthorization;
    private readonly IAgentDelegationService _agentDelegationService;

    public EffectiveAgentAuthorizationService(
        IExecutionContextAccessor executionContext,
        IUserAuthorizationService userAuthorization,
        IAgentAuthorizationService agentAuthorization,
        IAgentDelegationService agentDelegationService)
    {
        _executionContext = executionContext;
        _userAuthorization = userAuthorization;
        _agentAuthorization = agentAuthorization;
        _agentDelegationService = agentDelegationService;
    }

    public async Task<EffectivePermissionResult> AuthorizeAsync(
        string permission,
        CancellationToken cancellationToken = default)
    {
        var context = _executionContext.Current;
        if (context == null)
        {
            return new EffectivePermissionResult
            {
                Allowed = false,
                Reason = "Execution context is not established."
            };
        }

        var actor = context.Actor;
        var agent = context.Actor?.Agent;

        if (agent == null || string.IsNullOrWhiteSpace(agent.AgentId))
        {
            return new EffectivePermissionResult
            {
                Allowed = false,
                Reason = "Agent identity is not established."
            };
        }

        if (!actor.UserId.HasValue)
        {
            return new EffectivePermissionResult
            {
                Allowed = false,
                Reason = "User identity is not established."
            };

        }

        if (!actor.DelegationId.HasValue)
        {
            return new EffectivePermissionResult
            {
                Allowed = false,
                Reason = "Delegation identity is not established."
            };
        }

        // ---------------------------------------------
        // 1. User authorization
        // ---------------------------------------------

        if (!_userAuthorization.HasPermission(permission))
        {
            return new EffectivePermissionResult
            {
                Allowed = false,
                Reason ="User does not have the required permission."
            };
        }

        // ---------------------------------------------
        // 2. Agent delegation authorization
        // ---------------------------------------------

        var agentAllowed =
           await _agentAuthorization.HasPermissionAsync(
                agent,
                permission,
                cancellationToken);

        if (!agentAllowed)
        {
            return new EffectivePermissionResult
            {
                Allowed = false,
                Reason ="Agent does not have the required permission."
            };
        }

        var delegation = await _agentDelegationService.GetForCurrentUserAsync(
               actor.DelegationId.Value,
               cancellationToken);

        delegation = await _agentDelegationService.ValidateAsync(
               delegation.DelegationId,
               cancellationToken);

        if (!string.Equals(
               delegation.AgentId,
               actor.Agent?.AgentId,
               StringComparison.OrdinalIgnoreCase))
        {
            return new EffectivePermissionResult
            {
                Allowed = false,
                Reason = "Agent identity does not match the delegation."
            };
        }

        if (!delegation.Scopes.Contains(
                permission))
        {
            return new EffectivePermissionResult
            {
                Allowed = false,
                Reason = "The permission was not delegated to the agent."
            };
        }

        // ---------------------------------------------
        // 3. User ∩ Agent
        // ---------------------------------------------

        return new EffectivePermissionResult
        {
            Allowed = true
        };
    }
}