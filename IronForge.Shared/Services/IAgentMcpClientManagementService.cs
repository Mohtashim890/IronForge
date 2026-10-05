using IronForge.Shared.Models;
using IronForge.Shared.Models.Agents;

namespace IronForge.Shared.Services;

public interface IAgentMcpClientManagementService
{
    Task<ServiceResult<List<AgentMcpClientLinkDto>>> GetLinksAsync(
        string agentId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<AgentMcpClientLinkDto>> LinkAsync(
        string agentId,
        string clientId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<bool>> UnlinkAsync(
        string agentId,
        string clientId,
        CancellationToken cancellationToken = default);
}
