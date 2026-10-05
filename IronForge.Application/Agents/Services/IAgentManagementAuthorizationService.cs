using IronForge.Application.Auth.Models;

namespace IronForge.Application.Agents.Services
{
    public interface IAgentManagementAuthorizationService
    {
        Task<ResourceAuthorizationResult> AuthorizeAsync(
            string requiredPermission,
            CancellationToken cancellationToken = default);
    }
}
