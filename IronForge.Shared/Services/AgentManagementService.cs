using IronForge.Shared.Models;
using IronForge.Shared.Models.Agents.Manage;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace IronForge.Shared.Services
{
    public sealed class AgentManagementService : IAgentManagementService
    {
        private readonly HttpClient _httpClient;

        public AgentManagementService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<ServiceResult<List<AgentDto>>> GetAgentsAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var response = await _httpClient.GetAsync(
                    "api/agent", cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return ServiceResult<List<AgentDto>>.Fail(
                        await GetErrorMessageAsync(response, "Unable to load agents."),
                        (int)response.StatusCode);

                var agents = await response.Content.ReadFromJsonAsync<List<AgentDto>>(
                    cancellationToken: cancellationToken);

                return ServiceResult<List<AgentDto>>.Ok(agents ?? []);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                return ServiceResult<List<AgentDto>>.Fail("Unable to connect to the API.");
            }
            catch (Exception)
            {
                return ServiceResult<List<AgentDto>>.Fail(
                    "An unexpected error occurred while loading agents.");
            }
        }

        public async Task<ServiceResult<AgentDto>> GetAgentAsync(
            string agentId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var response = await _httpClient.GetAsync(
                    $"api/agent/{Uri.EscapeDataString(agentId)}",
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return ServiceResult<AgentDto>.Fail(
                        await GetErrorMessageAsync(response, "Unable to load the agent."),
                        (int)response.StatusCode);

                var agent = await response.Content.ReadFromJsonAsync<AgentDto>(
                    cancellationToken: cancellationToken);

                return agent is null
                    ? ServiceResult<AgentDto>.Fail(
                        "The API returned an invalid agent response.",
                        (int)HttpStatusCode.InternalServerError)
                    : ServiceResult<AgentDto>.Ok(agent);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                return ServiceResult<AgentDto>.Fail("Unable to connect to the API.");
            }
            catch (Exception)
            {
                return ServiceResult<AgentDto>.Fail(
                    "An unexpected error occurred while loading the agent.");
            }
        }

        public async Task<ServiceResult<AgentDto>> CreateAgentAsync(
            CreateAgentRequest request,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var response = await _httpClient.PostAsJsonAsync(
                    "api/agent", request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return ServiceResult<AgentDto>.Fail(
                        await GetErrorMessageAsync(response, "Unable to create the agent."),
                        (int)response.StatusCode);

                var agent = await response.Content.ReadFromJsonAsync<AgentDto>(
                    cancellationToken: cancellationToken);

                return agent is null
                    ? ServiceResult<AgentDto>.Fail(
                        "The API returned an invalid agent response.",
                        (int)HttpStatusCode.InternalServerError)
                    : ServiceResult<AgentDto>.Ok(agent);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                return ServiceResult<AgentDto>.Fail("Unable to connect to the API.");
            }
            catch (Exception)
            {
                return ServiceResult<AgentDto>.Fail(
                    "An unexpected error occurred while creating the agent.");
            }
        }

        public async Task<ServiceResult<AgentDto>> UpdateAgentAsync(
            string agentId,
            UpdateAgentRequest request,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var response = await _httpClient.PutAsJsonAsync(
                    $"api/agent/{Uri.EscapeDataString(agentId)}",
                    request,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return ServiceResult<AgentDto>.Fail(
                        await GetErrorMessageAsync(response, "Unable to update the agent."),
                        (int)response.StatusCode);

                var agent = await response.Content.ReadFromJsonAsync<AgentDto>(
                    cancellationToken: cancellationToken);

                return agent is null
                    ? ServiceResult<AgentDto>.Fail(
                        "The API returned an invalid agent response.",
                        (int)HttpStatusCode.InternalServerError)
                    : ServiceResult<AgentDto>.Ok(agent);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                return ServiceResult<AgentDto>.Fail("Unable to connect to the API.");
            }
            catch (Exception)
            {
                return ServiceResult<AgentDto>.Fail(
                    "An unexpected error occurred while updating the agent.");
            }
        }

        public async Task<ServiceResult<bool>> DeleteAgentAsync(
            string agentId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var response = await _httpClient.DeleteAsync(
                    $"api/agent/{Uri.EscapeDataString(agentId)}",
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return ServiceResult<bool>.Fail(
                        await GetErrorMessageAsync(response, "Unable to delete the agent."),
                        (int)response.StatusCode);

                return ServiceResult<bool>.Ok(true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                return ServiceResult<bool>.Fail("Unable to connect to the API.");
            }
            catch (Exception)
            {
                return ServiceResult<bool>.Fail(
                    "An unexpected error occurred while deleting the agent.");
            }
        }

        public async Task<ServiceResult<AgentDto>> UpdatePermissionsAsync(
            string agentId,
            UpdateAgentPermissionsRequest request,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var response = await _httpClient.PutAsJsonAsync(
                    $"api/agent/{Uri.EscapeDataString(agentId)}/permissions",
                    request,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return ServiceResult<AgentDto>.Fail(
                        await GetErrorMessageAsync(
                            response,
                            "Unable to update agent permissions."),
                        (int)response.StatusCode);

                var agent = await response.Content.ReadFromJsonAsync<AgentDto>(
                    cancellationToken: cancellationToken);

                return agent is null
                    ? ServiceResult<AgentDto>.Fail(
                        "The API returned an invalid agent response.",
                        (int)HttpStatusCode.InternalServerError)
                    : ServiceResult<AgentDto>.Ok(agent);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                return ServiceResult<AgentDto>.Fail("Unable to connect to the API.");
            }
            catch (Exception)
            {
                return ServiceResult<AgentDto>.Fail(
                    "An unexpected error occurred while updating agent permissions.");
            }
        }

        private static async Task<string> GetErrorMessageAsync(
            HttpResponseMessage response,
            string fallback)
        {
            try
            {
                var body = await response.Content.ReadAsStringAsync();

                if (string.IsNullOrWhiteSpace(body))
                    return fallback;

                try
                {
                    var message = JsonSerializer.Deserialize<string>(body);

                    if (!string.IsNullOrWhiteSpace(message))
                        return message;
                }
                catch (JsonException)
                {
                    // Current API may return plain text or a JSON string.
                }

                return body.Trim().Trim('"');
            }
            catch
            {
                return fallback;
            }
        }
    }
}
