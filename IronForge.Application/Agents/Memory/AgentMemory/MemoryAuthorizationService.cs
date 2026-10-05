using IronForge.Application.Agents.Authorization;
using IronForge.Application.Agents.Models;
using IronForge.Application.Auth.Models;
using IronForge.Application.Auth.Services;
using IronForge.Application.Execution;
using IronForge.Application.Tenants.Services;
using IronForge.Shared.Authorization;

namespace IronForge.Application.Agents.Memory.AgentMemory
{
    public class MemoryAuthorizationService
        : IMemoryAuthorizationService
    {
        private readonly IExecutionContextAccessor _executionContext;
        private readonly IUserAuthorizationService _userAuthorization;
        private readonly IAgentAuthorizationService _agentAuthorization;
        private readonly IEffectiveAgentAuthorizationService _effectiveAgentAuthorization;
        private readonly ITenantAuthorizationService _tenantAuthorization;

        public MemoryAuthorizationService(
            IExecutionContextAccessor executionContext,
            IUserAuthorizationService userAuthorization,
            IAgentAuthorizationService agentAuthorization,
            IEffectiveAgentAuthorizationService effectiveAgentAuthorization,
            ITenantAuthorizationService tenantAuthorization)
        {
            _executionContext = executionContext;
            _userAuthorization = userAuthorization;
            _agentAuthorization = agentAuthorization;
            _effectiveAgentAuthorization = effectiveAgentAuthorization;
            _tenantAuthorization = tenantAuthorization;
        }
        public async Task<ResourceAuthorizationResult> AuthorizeCollectionReadAsync(MemoryScope scope,
        CancellationToken cancellationToken = default)
        {
            var context = _executionContext.Current;

            if (context == null)
                return Deny("Execution context is not established.");

            if (!context.TenantId.HasValue)
                return Deny("Tenant context is not established.");

            var actor = context.Actor;

            if (actor.Agent?.Type == AgentType.External)
            {
                var result =
                    await _effectiveAgentAuthorization.AuthorizeAsync(
                        Permissions.MemoryRead,
                        cancellationToken);

                return result.Allowed
                    ? Allow()
                    : Deny(
                        result.Reason ??
                        "External agent is not authorized.");
            }

            if (actor.Agent?.Type == AgentType.Internal)
            {
                var allowed =
                    await _agentAuthorization.HasPermissionAsync(
                        actor.Agent,
                        Permissions.MemoryRead, 
                        cancellationToken);

                return allowed
                    ? Allow()
                    : Deny(
                        "Internal agent does not have the required permission.");
            }

            if (!actor.UserId.HasValue)
                return Deny("User identity is not established.");

            if (!_userAuthorization.HasPermission(
                    Permissions.MemoryRead))
            {
                return Deny(
                    "User does not have the required permission.");
            }

            return Allow();
        }

        public async Task<ResourceAuthorizationResult>AuthorizeCreateAsync(MemoryScope scope,
        CancellationToken cancellationToken = default)
        {
            var context = _executionContext.Current;

            if (context == null)
                return Deny("Execution context is not established.");

            if (!context.TenantId.HasValue)
                return Deny("Tenant context is not established.");

            var actor = context.Actor;

            if (actor.Agent?.Type == AgentType.External)
            {
                var result =
                    await _effectiveAgentAuthorization.AuthorizeAsync(
                        Permissions.MemoryCreate,
                        cancellationToken);

                return result.Allowed
                    ? Allow()
                    : Deny(
                        result.Reason ??
                        "External agent is not authorized.");
            }

            if (actor.Agent?.Type == AgentType.Internal)
            {
                var allowed =
                    await _agentAuthorization.HasPermissionAsync(
                        actor.Agent,
                        Permissions.MemoryCreate,
                        cancellationToken);

                return allowed
                    ? Allow()
                    : Deny(
                        "Internal agent does not have the required permission.");
            }

            if (!actor.UserId.HasValue)
                return Deny("User identity is not established.");

            if (!_userAuthorization.HasPermission(
                    Permissions.MemoryCreate))
            {
                return Deny(
                    "User does not have the required permission.");
            }

            return Allow();
        }

        public async Task<ResourceAuthorizationResult> AuthorizeReadAsync(Entities.AgentMemory memory,
        CancellationToken cancellationToken = default)
        {
            var context = _executionContext.Current;

            if (context == null)
                return Deny("Execution context is not established.");

            if (!context.TenantId.HasValue)
                return Deny("Tenant context is not established.");

            var tenantResult = _tenantAuthorization.Authorize(
        memory.TenantId);

            if (!tenantResult.Allowed)
                return tenantResult;

            var actor = context.Actor;
            if (actor.Agent?.Type == AgentType.External)
            {
                var result =
                    await _effectiveAgentAuthorization.AuthorizeAsync(
                        Permissions.MemoryRead,
                        cancellationToken);

                return result.Allowed
                    ? Allow()
                    : Deny(
                        result.Reason ??
                        "External agent is not authorized.");
            }
            else if (actor.Agent?.Type == AgentType.Internal)
            {
                var allowed =
                    await _agentAuthorization.HasPermissionAsync(
                        actor.Agent,
                        Permissions.MemoryRead,
                        cancellationToken);

                return allowed
                    ? Allow()
                    : Deny(
                        "Internal agent does not have the required permission.");
            }

            if (!actor.UserId.HasValue)
                return Deny("User identity is not established.");

            if (!_userAuthorization.HasPermission(
                    Permissions.MemoryRead))
            {
                return Deny(
                    "User does not have the required permission.");
            }

            // enforce Scope-based access control

            if (memory.Scope == MemoryScope.Tenant)
            {
                return Allow();
            }
            else if (memory.Scope == MemoryScope.User || memory.Scope == MemoryScope.Session)
            {
                if (memory.UserId != actor.UserId.Value)
                {
                    return Deny(
                        "You are not authorized to access this user memory.");
                }

                return Allow();
            }

            return Deny("Invalid memory scope.");
        }

        public async Task<ResourceAuthorizationResult>AuthorizeDeleteAsync(
        Entities.AgentMemory memory,
        CancellationToken cancellationToken = default)
        {
            var context = _executionContext.Current;

            if (context == null)
                return Deny("Execution context is not established.");

            if (!context.TenantId.HasValue)
                return Deny("Tenant context is not established.");

            var tenantResult =
                _tenantAuthorization.Authorize(
                    memory.TenantId);

            if (!tenantResult.Allowed)
                return tenantResult;

            var actor = context.Actor;

            if (actor.Agent?.Type == AgentType.External)
            {
                var result =
                    await _effectiveAgentAuthorization.AuthorizeAsync(
                        Permissions.MemoryDelete,
                        cancellationToken);

                return result.Allowed
                    ? Allow()
                    : Deny(
                        result.Reason ??
                        "External agent is not authorized.");
            }
            else if (actor.Agent?.Type == AgentType.Internal)
            {
                var allowed =
                    await _agentAuthorization.HasPermissionAsync(
                        actor.Agent,
                        Permissions.MemoryDelete,
                        cancellationToken);

                return allowed
                    ? Allow()
                    : Deny(
                        "Internal agent does not have the required permission.");
            }

            if (!actor.UserId.HasValue)
                return Deny("User identity is not established.");

            if (!_userAuthorization.HasPermission(
                    Permissions.MemoryDelete))
            {
                return Deny(
                    "User does not have the required permission.");
            }

            if (memory.Scope == MemoryScope.User ||
                memory.Scope == MemoryScope.Session)
            {
                if (memory.UserId != actor.UserId.Value)
                {
                    return Deny(
                        "You are not authorized to delete this memory.");
                }
            }

            return Allow();
        }

        private static ResourceAuthorizationResult Allow()
        {
            return new ResourceAuthorizationResult
            {
                Allowed = true
            };
        }

        private static ResourceAuthorizationResult Deny(
            string reason)
        {
            return new ResourceAuthorizationResult
            {
                Allowed = false,
                Reason = reason
            };
        }
    }
}