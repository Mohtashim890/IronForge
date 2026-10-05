namespace IronForge.Application.Agents.Authorization;

public interface IAgentMcpClientAuthorizationService
{
    Task<bool> IsAuthorizedAsync(
        string agentId,
        string clientId,
        CancellationToken cancellationToken = default);
}
