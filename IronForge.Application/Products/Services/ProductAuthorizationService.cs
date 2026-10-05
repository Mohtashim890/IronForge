using IronForge.Application.Agents.Authorization;
using IronForge.Application.Agents.Models;
using IronForge.Application.Auth.Models;
using IronForge.Application.Auth.Services;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.Tenants.Services;

namespace IronForge.Application.Products.Services;

public class ProductAuthorizationService
    : IProductAuthorizationService
{
    private readonly IExecutionContextAccessor _executionContext;
    private readonly IUserAuthorizationService _userAuthorization;
    private readonly IAgentAuthorizationService _agentAuthorization;
    private readonly ITenantAuthorizationService _tenantAuthorization;
    private readonly IEffectiveAgentAuthorizationService _effectiveAgentAuthorization;

    public ProductAuthorizationService(
        IExecutionContextAccessor executionContext,
        IUserAuthorizationService userAuthorization,
        IEffectiveAgentAuthorizationService effectiveAgentAuthorization,
        IAgentAuthorizationService agentAuthorization,
        ITenantAuthorizationService tenantAuthorization)
    {
        _executionContext = executionContext;
        _userAuthorization = userAuthorization;
        _effectiveAgentAuthorization = effectiveAgentAuthorization;
        _agentAuthorization = agentAuthorization;
        _tenantAuthorization = tenantAuthorization;
    }
    public async Task<ResourceAuthorizationResult> AuthorizeCollectionAsync(
    string requiredPermission,
    CancellationToken cancellationToken = default)
    {
        var context = _executionContext.Current;

        if (context == null)
        {
            return ResourceAuthorizationResult.Deny(
                "Execution context is not established.");
        }

        var actor = context.Actor;
        if (actor.Agent?.Type == AgentType.External)
        {
            var result =
                await _effectiveAgentAuthorization.AuthorizeAsync(
                    requiredPermission,
                    cancellationToken);

            return result.Allowed
                ? ResourceAuthorizationResult.Allow()
                : ResourceAuthorizationResult.Deny(
                    result.Reason ??
                    "External agent is not authorized.");
        }

        if (actor.Agent?.Type == AgentType.Internal)
        {
            var allowed =
                await _agentAuthorization.HasPermissionAsync(
                    actor.Agent,
                    requiredPermission,
                    cancellationToken);

            return allowed
                ? ResourceAuthorizationResult.Allow()
                : ResourceAuthorizationResult.Deny(
                    "Internal agent does not have the required permission.");
        }

        if (!actor.UserId.HasValue)
        {
            return ResourceAuthorizationResult.Deny(
                "User identity is not established.");
        }

        if (!_userAuthorization.HasPermission(requiredPermission))
        {
            return ResourceAuthorizationResult.Deny(
                "User does not have the required permission.");
        }

        return ResourceAuthorizationResult.Allow();
    }

    public async Task<ResourceAuthorizationResult> AuthorizeAsync(
        Product product,
        string requiredPermission,
        CancellationToken cancellationToken = default)
    {
        var context = _executionContext.Current;

        if (context == null)
        {
            return ResourceAuthorizationResult.Deny(
                "Execution context is not established.");
        }

        var tenantResult =
    _tenantAuthorization.Authorize(product.TenantId);

        if (!tenantResult.Allowed)
        {
            return tenantResult;
        }

        var actor = context.Actor;
        if (actor.Agent != null)
        {
            if (actor.Agent.Type == AgentType.External)
            {
                var result =
                    await _effectiveAgentAuthorization.AuthorizeAsync(
                        requiredPermission,
                        cancellationToken);

                if (!result.Allowed)
                {
                    return ResourceAuthorizationResult.Deny(
                        result.Reason ??
                        "External agent is not authorized.");
                }

                /*
                 * The effective authorization service has already
                 * established the required user + agent + delegation
                 * authorization.
                 *
                 * Resource ownership is intentionally not checked
                 * here because the delegated authorization represents
                 * the authority granted to this external agent.
                 */
                return ResourceAuthorizationResult.Allow();
            }

            if (actor.Agent.Type == AgentType.Internal)
            {
                var allowed =
                    await _agentAuthorization.HasPermissionAsync(
                        actor.Agent,
                        requiredPermission,
                        cancellationToken);

                if (!allowed)
                {
                    return ResourceAuthorizationResult.Deny(
                        "Internal agent does not have the required permission.");
                }

            }
        }

        if (!actor.UserId.HasValue)
        {
            return ResourceAuthorizationResult.Deny(
                "User identity is not established.");
        }
        if (!_userAuthorization.HasPermission(requiredPermission))
        {
            return ResourceAuthorizationResult.Deny(
                "User does not have the required permission.");
        }

        if (string.Equals(
            actor.Role,
            "Admin",
            StringComparison.OrdinalIgnoreCase))
        {
            return ResourceAuthorizationResult.Allow();
        }

        if (product.OwnerUserId != actor.UserId.Value)
        {
            return ResourceAuthorizationResult.Deny(
                "You are not authorized to access this product.");
        }

        return ResourceAuthorizationResult.Allow();
    }
}