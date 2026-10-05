using IronForge.Shared.Models;
using IronForge.Shared.Models.Agents;
using IronForge.Shared.Models.Agents.Workflows;
using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Text;

namespace IronForge.Shared.Services
{
    public class AgentWorkspaceService : IAgentWorkspaceService
    {
        private readonly HttpClient _httpClient;

        public AgentWorkspaceService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<ServiceResult<List<AgentSessionDto>>> GetSessionsAsync(string? agentId = null)
        {
            try
            {
                var response =
                    await _httpClient.GetAsync($"api/agent/sessions?agentId={agentId}");

                if (!response.IsSuccessStatusCode)
                {
                    return ServiceResult<List<AgentSessionDto>>.Fail(
                        "Unable to load agent sessions.",
                        (int)response.StatusCode);
                }

                var sessions =
                    await response.Content
                        .ReadFromJsonAsync<List<AgentSessionDto>>();

                return ServiceResult<List<AgentSessionDto>>.Ok(
                    sessions ?? []);
            }
            catch (Exception ex)
            {
                return ServiceResult<List<AgentSessionDto>>.Fail(
                    ex.Message);
            }
        }

        public async Task<ServiceResult<AgentSessionDto>> CreateSessionAsync(string agentId,
            string? title = null)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync(
                    "api/agent/sessions",
                    new CreateAgentSessionRequest
                    {
                        AgentId = agentId,
                        Title = title
                    });

                if (!response.IsSuccessStatusCode)
                {
                    return ServiceResult<AgentSessionDto>.Fail(
                        "Unable to create agent session.",
                        (int)response.StatusCode);
                }

                var session =
                    await response.Content
                        .ReadFromJsonAsync<AgentSessionDto>();

                if (session is null)
                {
                    return ServiceResult<AgentSessionDto>.Fail(
                        "The server returned an invalid session.");
                }

                return ServiceResult<AgentSessionDto>.Ok(session);
            }
            catch (Exception ex)
            {
                return ServiceResult<AgentSessionDto>.Fail(
                    ex.Message);
            }
        }

        public async Task<ServiceResult<bool>> ArchiveSessionAsync(
            Guid sessionId)
        {
            try
            {
                var response = await _httpClient.PostAsync(
                    $"api/agent/sessions/{sessionId}/archive",
                    null);

                if (!response.IsSuccessStatusCode)
                {
                    return ServiceResult<bool>.Fail(
                        "Unable to archive agent session.",
                        (int)response.StatusCode);
                }

                return ServiceResult<bool>.Ok(true);
            }
            catch (Exception ex)
            {
                return ServiceResult<bool>.Fail(
                    ex.Message);
            }
        }
        public async Task<ServiceResult<List<ConversationMessageDto>>> GetMessagesAsync(Guid sessionId)
        {
            try
            {
                var response =
                    await _httpClient.GetAsync(
                        $"api/agent/sessions/{sessionId}/messages");

                if (!response.IsSuccessStatusCode)
                {
                    return ServiceResult<List<ConversationMessageDto>>.Fail(
                        "Unable to load conversation messages.",
                        (int)response.StatusCode);
                }

                var messages =
                    await response.Content
                        .ReadFromJsonAsync<List<ConversationMessageDto>>();

                return ServiceResult<List<ConversationMessageDto>>.Ok(
                    messages ?? []);
            }
            catch (Exception ex)
            {
                return ServiceResult<List<ConversationMessageDto>>.Fail(
                    ex.Message);
            }
        }

        public async Task<ServiceResult<AgentSessionDto>> GetSessionAsync(Guid sessionId)
        {
            try
            {
                var response =
                    await _httpClient.GetAsync(
                        $"api/agent/sessions/{sessionId}");

                if (!response.IsSuccessStatusCode)
                {
                    return ServiceResult<AgentSessionDto>.Fail(
                        "Unable to load agent session.",
                        (int)response.StatusCode);
                }

                var session =
                    await response.Content
                        .ReadFromJsonAsync<AgentSessionDto>();

                return ServiceResult<AgentSessionDto>.Ok(session);
            }
            catch (Exception ex)
            {
                return ServiceResult<AgentSessionDto>.Fail(
                    ex.Message);
            }
        }
        public async Task<ServiceResult<AgentRunResultDto>> RunProductAgentAsync(
        Guid sessionId,
        string message)
        {
            try
            {
                var request = new
                {
                    SessionId = sessionId,
                    Message = message
                };

                var response = await _httpClient.PostAsJsonAsync(
                    "api/agent/product",
                    request);

                if (!response.IsSuccessStatusCode)
                {
                    var error =
                        await response.Content.ReadAsStringAsync();

                    return ServiceResult<AgentRunResultDto>.Fail(
                        string.IsNullOrWhiteSpace(error)
                            ? "Unable to run the Product Agent."
                            : error,
                        (int)response.StatusCode);
                }

                var result =
                    await response.Content
                        .ReadFromJsonAsync<AgentRunResultDto>();

                if (result is null)
                {
                    return ServiceResult<AgentRunResultDto>.Fail(
                        "The Product Agent returned an invalid response.");
                }

                return ServiceResult<AgentRunResultDto>.Ok(result);
            }
            catch (Exception ex)
            {
                return ServiceResult<AgentRunResultDto>.Fail(
                    ex.Message);
            }
        }

        public async Task<ServiceResult<AgentApprovalDecisionResultDto>>RespondToApprovalAsync(
        AgentApprovalDecisionDto decision)
        {
            try
            {
                var response =
                    await _httpClient.PostAsJsonAsync(
                        $"api/agent/product/approvals/{decision.ApprovalId}/decision",
                        decision);

                if (!response.IsSuccessStatusCode)
                {
                    var error =
                        await response.Content.ReadAsStringAsync();

                    return ServiceResult<AgentApprovalDecisionResultDto>
                        .Fail(
                            string.IsNullOrWhiteSpace(error)
                                ? "Unable to process the approval decision."
                                : error,
                            (int)response.StatusCode);
                }

                var result =
                    await response.Content
                        .ReadFromJsonAsync<
                            AgentApprovalDecisionResultDto>();

                if (result is null)
                {
                    return ServiceResult<AgentApprovalDecisionResultDto>
                        .Fail(
                            "The server returned an invalid approval response.");
                }

                return ServiceResult<AgentApprovalDecisionResultDto>
                    .Ok(result);
            }
            catch (Exception ex)
            {
                return ServiceResult<AgentApprovalDecisionResultDto>
                    .Fail(ex.Message);
            }
        }

        public async Task<ServiceResult<AgentWorkflowDto?>>
    GetWorkflowAsync(Guid sessionId)
        {
            try
            {
                var response =
                    await _httpClient.GetAsync(
                        $"api/agent/sessions/{sessionId}/workflow");

                if (!response.IsSuccessStatusCode)
                {
                    return new ServiceResult<AgentWorkflowDto?>
                    {
                        Success = false,
                        StatusCode = (int)response.StatusCode,
                        ErrorMessage = "Unable to load workflow."
                    };
                }

                var workflow =
                    await response.Content
                        .ReadFromJsonAsync<AgentWorkflowDto>();

                return new ServiceResult<AgentWorkflowDto?>
                {
                    Success = true,
                    StatusCode = (int)response.StatusCode,
                    Data = workflow
                };
            }
            catch (Exception ex)
            {
                return new ServiceResult<AgentWorkflowDto?>
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }
    }
}
