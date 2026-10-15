using IronForge.Application.Agents.DTOs;
using IronForge.Application.Commons;
using IronForge.Shared.Models.Agents;
using IronForge.Shared.Models.Agents.Manage;
using AgentDto = IronForge.Application.Agents.DTOs.AgentDto;
using CreateAgentRequest = IronForge.Application.Agents.DTOs.CreateAgentRequest;
using UpdateAgentPermissionsRequest = IronForge.Application.Agents.DTOs.UpdateAgentPermissionsRequest;
using UpdateAgentRequest = IronForge.Application.Agents.DTOs.UpdateAgentRequest;

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

        Task<ServiceResult<List<AgentToolCatalogItemDto>>> GetToolCatalogAsync(
            CancellationToken cancellationToken = default);

        Task<ServiceResult<AgentDto>> UpdateToolsAsync(
            string agentId,
            UpdateAgentToolsRequest request,
            CancellationToken cancellationToken = default);

        Task<ServiceResult<List<AgentModelProfileDto>>> GetModelProfilesAsync(
            CancellationToken cancellationToken = default);

        Task<ServiceResult<AgentUsageSummaryDto>> GetUsageAsync(
            CancellationToken cancellationToken = default);

        Task<ServiceResult<List<AvailableAgentDto>>> GetAvailableAgentsAsync(
            CancellationToken cancellationToken = default);
    }
}
