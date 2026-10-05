using IronForge.Application.Entities;
using IronForge.Application.Persistence;

namespace IronForge.Application.Agents.Authorization;

public sealed class AgentMcpClientAuthorizationService
    : IAgentMcpClientAuthorizationService
{
    private readonly IRepository<AgentMcpClientAuthorization> _authorizations;
    private readonly IRepository<Agent> _agents;
    private readonly IRepository<McpClient> _clients;

    public AgentMcpClientAuthorizationService(
        IRepository<AgentMcpClientAuthorization> authorizations,
        IRepository<Agent> agents,
        IRepository<McpClient> clients)
    {
        _authorizations = authorizations;
        _agents = agents;
        _clients = clients;
    }

    public async Task<bool> IsAuthorizedAsync(
        string agentId,
        string clientId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agentId) ||
            string.IsNullOrWhiteSpace(clientId))
            return false;

        var agent = await _agents.FirstOrDefaultAsync(
            x => x.AgentId == agentId && x.IsActive,
            cancellationToken);

        if (agent is null)
            return false;

        var client = await _clients.FirstOrDefaultAsync(
            x => x.ClientId == clientId && x.IsActive,
            cancellationToken);

        if (client is null)
            return false;

        return await _authorizations.AnyAsync(
            x =>
                x.AgentEntityId == agent.Id &&
                x.McpClientEntityId == client.Id &&
                x.IsActive,
            cancellationToken);
    }
}
