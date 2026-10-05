using IronForge.Application.Agents.DTOs;
using IronForge.Application.Commons;

namespace IronForge.Application.Agents.Services
{
    public interface IAgentManagementService
    {
        Task<ServiceResult<List<AgentDto>>> GetAgentsAsync(
            CancellationToken cancellationToken = default);

        Task<ServiceResult<AgentDto>> GetAgentAsync(
            string agentId,
            CancellationToken cancellationToken = default);

        Task<ServiceResult<AgentDto>> CreateAgentAsync(
            CreateAgentRequest request,
            CancellationToken cancellationToken = default);

        Task<ServiceResult<AgentDto>> UpdateAgentAsync(
            string agentId,
            UpdateAgentRequest request,
            CancellationToken cancellationToken = default);

        Task<ServiceResult<bool>> DeleteAgentAsync(
            string agentId,
            CancellationToken cancellationToken = default);

        Task<ServiceResult<AgentDto>> UpdatePermissionsAsync(
            string agentId,
            UpdateAgentPermissionsRequest request,
            CancellationToken cancellationToken = default);
    }
}
