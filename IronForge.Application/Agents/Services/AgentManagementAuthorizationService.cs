using IronForge.Application.Auth.Models;
using IronForge.Application.Auth.Services;
using IronForge.Application.Execution;

namespace IronForge.Application.Agents.Services
{
    public sealed class AgentManagementAuthorizationService
        : IAgentManagementAuthorizationService
    {
        private readonly IExecutionContextAccessor _executionContext;
        private readonly IUserAuthorizationService _userAuthorization;

        public AgentManagementAuthorizationService(
            IExecutionContextAccessor executionContext,
            IUserAuthorizationService userAuthorization)
        {
            _executionContext = executionContext;
            _userAuthorization = userAuthorization;
        }

        public Task<ResourceAuthorizationResult> AuthorizeAsync(
            string requiredPermission,
            CancellationToken cancellationToken = default)
        {
            var context = _executionContext.Current;

            if (context == null)
            {
                return Task.FromResult(
                    ResourceAuthorizationResult.Deny(
                        "Execution context is not established."));
            }

            if (!context.Actor.UserId.HasValue)
            {
                return Task.FromResult(
                    ResourceAuthorizationResult.Deny(
                        "User identity is not established."));
            }

            if (!_userAuthorization.HasPermission(
                    requiredPermission))
            {
                return Task.FromResult(
                    ResourceAuthorizationResult.Deny(
                        "User does not have the required permission."));
            }

            return Task.FromResult(
                ResourceAuthorizationResult.Allow());
        }
    }
}
