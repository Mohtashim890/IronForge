using IronForge.Application.Agents.DTOs;
using IronForge.Application.Commons;
using IronForge.Shared.Models.Agents;
using IronForge.Shared.Models.Agents.Manage;
using AgentDto = IronForge.Application.Agents.DTOs.AgentDto;

namespace IronForge.Application.Agents.Services
{
    public interface IAgentManagementService
    {
        Task<ServiceResult<List<AgentDto>>> GetAgentsAsync(
            CancellationToken cancellationToken = default);

        Task<ServiceResult<AgentDto>> GetAgentAsync(
            string agentId,
            CancellationToken cancellationToken = default);

        /// <summary>Turns a built-in agent on or off for the gym.</summary>
        Task<ServiceResult<AgentDto>> SetActiveAsync(
            string agentId,
            bool isActive,
            CancellationToken cancellationToken = default);

        Task<ServiceResult<AgentUsageSummaryDto>> GetUsageAsync(
            CancellationToken cancellationToken = default);

        Task<ServiceResult<List<AvailableAgentDto>>> GetAvailableAgentsAsync(
            CancellationToken cancellationToken = default);
    }
}
