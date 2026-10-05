using IronForge.Application.Agents.Models;

namespace IronForge.Application.Agents.Authorization;

public interface IAgentAuthorizationService
{
    public Task<bool> HasPermissionAsync(AgentIdentity agent,
        string permission, CancellationToken cancellationToken);
}