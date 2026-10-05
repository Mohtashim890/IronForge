using IronForge.Application.Agents.Authorization;
using IronForge.Application.Agents.Delegations;
using IronForge.Application.Agents.Models;
using IronForge.Application.Execution;

namespace IronForge.Mcp.Authorization;

public sealed class McpAuthorizationService
    : IMcpAuthorizationService
{
    private readonly IExecutionContextAccessor _executionContext;
    private readonly IAgentDelegationService _delegationService;
    private readonly IAgentMcpClientAuthorizationService _agentMcpClientAuthorization;
    private readonly IEffectiveAgentAuthorizationService _effectiveAgentAuthorization;
    private readonly IMcpToolPermissionRegistry _toolPermissionRegistry;

    public McpAuthorizationService(
        IExecutionContextAccessor executionContext,
        IAgentDelegationService delegationService,
        IAgentMcpClientAuthorizationService agentMcpClientAuthorization,
        IEffectiveAgentAuthorizationService effectiveAgentAuthorization,
        IMcpToolPermissionRegistry toolPermissionRegistry)
    {
        _executionContext = executionContext;
        _delegationService = delegationService;
        _agentMcpClientAuthorization = agentMcpClientAuthorization;
        _effectiveAgentAuthorization = effectiveAgentAuthorization;
        _toolPermissionRegistry = toolPermissionRegistry;
    }

    public async Task<(bool Allowed, string Reason)> AuthorizeConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        var context = _executionContext.Current;

        if (context?.Actor == null)
            return (false, "Execution context is not established.");

        var actor = context.Actor;

        if (actor.Agent?.Type != AgentType.External)
            return (false, "MCP requests require an external agent identity.");

        if (string.IsNullOrWhiteSpace(actor.Agent.AgentId))
            return (false, "Agent identity is missing.");

        if (string.IsNullOrWhiteSpace(actor.ClientId))
            return (false, "MCP client identity is missing.");

        if (!actor.UserId.HasValue)
            return (false, "User identity is missing.");

        if (!actor.DelegationId.HasValue)
            return (false, "Delegation identity is missing.");

        var delegation =
            await _delegationService.ValidateAsync(
                actor.DelegationId.Value,
                cancellationToken);

        if (delegation.UserId != actor.UserId.Value)
        {
            return (
                false,
                "User identity does not match the delegation.");
        }

        if (!string.Equals(
                delegation.AgentId,
                actor.Agent.AgentId,
                StringComparison.OrdinalIgnoreCase))
        {
            return (
                false,
                "Agent identity does not match the delegation.");
        }

        if (!string.Equals(
                delegation.ClientId,
                actor.ClientId,
                StringComparison.OrdinalIgnoreCase))
        {
            return (
                false,
                "MCP client identity does not match the delegation.");
        }

        var clientAuthorized =
            await _agentMcpClientAuthorization.IsAuthorizedAsync(
                actor.Agent.AgentId,
                actor.ClientId,
                cancellationToken);

        if (!clientAuthorized)
        {
            return (
                false,
                "The MCP client is not authorized for this agent.");
        }

        return (true, string.Empty);
    }

    public async Task<(bool Allowed, string Reason)> AuthorizeToolAsync(
        string toolName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return (false, "MCP tool identity is missing.");
        }

        if (!_toolPermissionRegistry.TryGetRequiredPermission(
                toolName,
                out var requiredPermission))
        {
            return (
                false,
                $"MCP tool '{toolName}' is not authorized because no permission mapping exists.");
        }

        // D9 connection authorization is enforced by McpIdentityMiddleware
        // before MCP request handling reaches the CallTool filter.
        var context = _executionContext.Current;

        if (context?.Actor == null ||
            context.Actor.Agent?.Type != AgentType.External)
        {
            return (
                false,
                "External MCP execution context is not established.");
        }

        var capabilityAuthorization =
            await _effectiveAgentAuthorization.AuthorizeAsync(
                requiredPermission,
                cancellationToken);

        if (!capabilityAuthorization.Allowed)
        {
            return (
                false,
                capabilityAuthorization.Reason ??
                $"The MCP client is not authorized for permission '{requiredPermission}'.");
        }

        return (true, string.Empty);
    }
}
