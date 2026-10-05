namespace IronForge.Application.Agents.Authorization;

public interface IAgentPermissionRegistry
{
    Task<IReadOnlySet<string>> GetPermissionsAsync(
        string agentId,
        CancellationToken cancellationToken = default);
}