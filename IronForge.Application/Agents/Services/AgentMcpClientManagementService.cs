using IronForge.Application.Agents.DTOs;
using IronForge.Application.Commons;
using IronForge.Application.Entities;
using IronForge.Application.Persistence;

namespace IronForge.Application.Agents.Services;

public sealed class AgentMcpClientManagementService
    : IAgentMcpClientManagementService
{
    private readonly IRepository<Agent> _agents;
    private readonly IRepository<McpClient> _clients;
    private readonly IRepository<AgentMcpClientAuthorization> _authorizations;
    private readonly IAgentManagementAuthorizationService _authorization;

    public AgentMcpClientManagementService(
        IRepository<Agent> agents,
        IRepository<McpClient> clients,
        IRepository<AgentMcpClientAuthorization> authorizations,
        IAgentManagementAuthorizationService authorization)
    {
        _agents = agents;
        _clients = clients;
        _authorizations = authorizations;
        _authorization = authorization;
    }

    public async Task<ServiceResult<List<AgentMcpClientLinkDto>>> GetLinksAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _authorization.AuthorizeAsync(
            Permissions.AgentsMcpClientsManage, cancellationToken);

        if (!authorization.Allowed)
            return ServiceResult<List<AgentMcpClientLinkDto>>.Fail(
                authorization.Reason ??
                "You are not authorized to manage agent MCP clients.", 403);

        if (string.IsNullOrWhiteSpace(agentId))
            return ServiceResult<List<AgentMcpClientLinkDto>>.Fail(
                "Agent ID is required.", 400);

        var agent = await _agents.FirstOrDefaultAsync(
            x => x.AgentId == agentId, cancellationToken);

        if (agent is null)
            return ServiceResult<List<AgentMcpClientLinkDto>>.Fail(
                "Agent not found.", 404);

        var relationships = await _authorizations.ListAsync(
            x => x.AgentEntityId == agent.Id,
            cancellationToken);

        var clientIds = relationships
            .Select(x => x.McpClientEntityId)
            .Distinct()
            .ToList();

        var clients = clientIds.Count == 0
            ? new List<McpClient>()
            : await _clients.ListAsync(
                x => clientIds.Contains(x.Id),
                cancellationToken);

        var clientById = clients.ToDictionary(x => x.Id);

        var links = relationships
            .Where(x => clientById.ContainsKey(x.McpClientEntityId))
            .Select(x =>
            {
                var client = clientById[x.McpClientEntityId];

                return new AgentMcpClientLinkDto
                {
                    AgentId = agent.AgentId,
                    ClientId = client.ClientId,
                    ClientName = client.Name,
                    IsActive = x.IsActive && client.IsActive,
                    CreatedAtUtc = x.CreatedAtUtc,
                    UpdatedAtUtc = x.UpdatedAtUtc
                };
            })
            .OrderBy(x => x.ClientName)
            .ToList();

        return ServiceResult<List<AgentMcpClientLinkDto>>.Ok(links);
    }

    public async Task<ServiceResult<AgentMcpClientLinkDto>> LinkAsync(
        string agentId,
        string clientId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _authorization.AuthorizeAsync(
            Permissions.AgentsMcpClientsManage, cancellationToken);

        if (!authorization.Allowed)
            return ServiceResult<AgentMcpClientLinkDto>.Fail(
                authorization.Reason ??
                "You are not authorized to manage agent MCP clients.", 403);

        if (string.IsNullOrWhiteSpace(agentId) ||
            string.IsNullOrWhiteSpace(clientId))
            return ServiceResult<AgentMcpClientLinkDto>.Fail(
                "Agent ID and client ID are required.", 400);

        var agent = await _agents.FirstOrDefaultAsync(
            x => x.AgentId == agentId, cancellationToken);

        if (agent is null)
            return ServiceResult<AgentMcpClientLinkDto>.Fail(
                "Agent not found.", 404);

        var client = await _clients.FirstOrDefaultAsync(
            x => x.ClientId == clientId, cancellationToken);

        if (client is null)
            return ServiceResult<AgentMcpClientLinkDto>.Fail(
                "MCP client not found.", 404);

        if (agent.TenantId != client.TenantId)
            return ServiceResult<AgentMcpClientLinkDto>.Fail(
                "The agent and MCP client must belong to the same tenant.", 403);

        var relationship =
            await _authorizations.FirstOrDefaultTrackedAsync(
                x =>
                    x.AgentEntityId == agent.Id &&
                    x.McpClientEntityId == client.Id,
                cancellationToken);

        if (relationship is null)
        {
            relationship = new AgentMcpClientAuthorization
            {
                TenantId = agent.TenantId,
                AgentEntityId = agent.Id,
                McpClientEntityId = client.Id,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            _authorizations.Add(relationship);
        }
        else
        {
            relationship.IsActive = true;
            relationship.UpdatedAtUtc = DateTime.UtcNow;
            //_authorizations.Update(relationship);
        }

        await _authorizations.SaveChangesAsync(cancellationToken);

        return ServiceResult<AgentMcpClientLinkDto>.Ok(
            new AgentMcpClientLinkDto
            {
                AgentId = agent.AgentId,
                ClientId = client.ClientId,
                ClientName = client.Name,
                IsActive = relationship.IsActive && client.IsActive,
                CreatedAtUtc = relationship.CreatedAtUtc,
                UpdatedAtUtc = relationship.UpdatedAtUtc
            });
    }

    public async Task<ServiceResult<bool>> UnlinkAsync(
        string agentId,
        string clientId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _authorization.AuthorizeAsync(
            Permissions.AgentsMcpClientsManage, cancellationToken);

        if (!authorization.Allowed)
            return ServiceResult<bool>.Fail(
                authorization.Reason ??
                "You are not authorized to manage agent MCP clients.", 403);

        if (string.IsNullOrWhiteSpace(agentId) ||
            string.IsNullOrWhiteSpace(clientId))
            return ServiceResult<bool>.Fail(
                "Agent ID and client ID are required.", 400);

        var agent = await _agents.FirstOrDefaultAsync(
            x => x.AgentId == agentId, cancellationToken);

        if (agent is null)
            return ServiceResult<bool>.Fail(
                "Agent MCP client relationship not found.", 404);

        var client = await _clients.FirstOrDefaultAsync(
            x => x.ClientId == clientId, cancellationToken);

        if (client is null)
            return ServiceResult<bool>.Fail(
                "Agent MCP client relationship not found.", 404);

        var relationship =
            await _authorizations.FirstOrDefaultTrackedAsync(
                x =>
                    x.AgentEntityId == agent.Id &&
                    x.McpClientEntityId == client.Id,
                cancellationToken);

        if (relationship is null)
            return ServiceResult<bool>.Fail(
                "Agent MCP client relationship not found.", 404);

        relationship.IsActive = false;
        relationship.UpdatedAtUtc = DateTime.UtcNow;

        //_authorizations.Update(relationship);

        await _authorizations.SaveChangesAsync(cancellationToken);

        return ServiceResult<bool>.Ok(true);
    }
}
