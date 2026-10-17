using IronForge.Shared.Models;
using IronForge.Shared.Models.Agents.Manage;
using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Shared.Services
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


        Task<ServiceResult<List<AgentToolCatalogItemDto>>> GetToolCatalogAsync(
            CancellationToken cancellationToken = default);

        Task<ServiceResult<AgentDto>> DetachAsync(
            string agentId,
            CancellationToken cancellationToken = default);

        Task<ServiceResult<AgentDto>> UpdateToolsAsync(
            string agentId,
            UpdateAgentToolsRequest request,
            CancellationToken cancellationToken = default);

        Task<ServiceResult<List<AgentModelProfileDto>>> GetModelProfilesAsync(
            CancellationToken cancellationToken = default);

        Task<ServiceResult<AgentUsageSummaryDto>> GetUsageAsync(
            CancellationToken cancellationToken = default);
    }
}
