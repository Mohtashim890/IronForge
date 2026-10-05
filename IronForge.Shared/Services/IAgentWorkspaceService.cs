using IronForge.Shared.Models;
using IronForge.Shared.Models.Agents;
using IronForge.Shared.Models.Agents.Workflows;
using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Shared.Services
{
    public interface IAgentWorkspaceService
    {
        Task<ServiceResult<List<AgentSessionDto>>> GetSessionsAsync(
        string? agentId = null);

        Task<ServiceResult<AgentSessionDto>> CreateSessionAsync(
            string agentId,
            string? title = null);

        Task<ServiceResult<AgentSessionDto>> GetSessionAsync(
            Guid sessionId);

        Task<ServiceResult<List<ConversationMessageDto>>> GetMessagesAsync(
            Guid sessionId);

        Task<ServiceResult<bool>> ArchiveSessionAsync(
            Guid sessionId);

        Task<ServiceResult<AgentRunResultDto>> RunProductAgentAsync(Guid sessionId,
            string message);

        Task<ServiceResult<AgentApprovalDecisionResultDto>>RespondToApprovalAsync(
        AgentApprovalDecisionDto decision);

        Task<ServiceResult<AgentWorkflowDto?>>GetWorkflowAsync(Guid sessionId);

    }
}
