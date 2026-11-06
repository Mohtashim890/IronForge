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

        Task<ServiceResult<AgentDto>> SetActiveAsync(
            string agentId,
            bool isActive,
            CancellationToken cancellationToken = default);

        Task<ServiceResult<AgentUsageSummaryDto>> GetUsageAsync(
            CancellationToken cancellationToken = default);
    }
}
