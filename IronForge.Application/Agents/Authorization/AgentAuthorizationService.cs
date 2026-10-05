using IronForge.Application.Agents.Models;

namespace IronForge.Application.Agents.Authorization;

public class AgentAuthorizationService
    : IAgentAuthorizationService
{
    private readonly IAgentPermissionRegistry _registry;

    public AgentAuthorizationService(
        IAgentPermissionRegistry registry)
    {
        _registry = registry;
    }

    public async Task<bool> HasPermissionAsync(
    AgentIdentity agent,
    string permission,
    CancellationToken cancellationToken = default)
    {
        var permissions =
            await _registry.GetPermissionsAsync(
                agent.AgentId,
                cancellationToken);

        return permissions.Contains(permission);
    }
}