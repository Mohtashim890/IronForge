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

        public async Task<ServiceResult<List<AgentToolCatalogItemDto>>> GetToolCatalogAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var response = await _httpClient.GetAsync(
                    "api/agent/tools", cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return ServiceResult<List<AgentToolCatalogItemDto>>.Fail(
                        await GetErrorMessageAsync(response, "Unable to load the tool catalogue."),
                        (int)response.StatusCode);

                var tools = await response.Content.ReadFromJsonAsync<List<AgentToolCatalogItemDto>>(
                    cancellationToken: cancellationToken);

                return ServiceResult<List<AgentToolCatalogItemDto>>.Ok(tools ?? []);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                return ServiceResult<List<AgentToolCatalogItemDto>>.Fail("Unable to connect to the API.");
            }
            catch (Exception)
            {
                return ServiceResult<List<AgentToolCatalogItemDto>>.Fail(
                    "An unexpected error occurred while loading the tool catalogue.");
            }
        }

        public async Task<ServiceResult<AgentDto>> DetachAsync(
            string agentId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var response = await _httpClient.PostAsync(
                    $"api/agent/{Uri.EscapeDataString(agentId)}/detach",
                    content: null,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return ServiceResult<AgentDto>.Fail(
                        await GetErrorMessageAsync(response, "Unable to detach the agent."),
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
                    "An unexpected error occurred while detaching the agent.");
            }
        }

        public async Task<ServiceResult<AgentDto>> UpdateToolsAsync(
            string agentId,
            UpdateAgentToolsRequest request,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var response = await _httpClient.PutAsJsonAsync(
                    $"api/agent/{Uri.EscapeDataString(agentId)}/tools",
                    request,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return ServiceResult<AgentDto>.Fail(
                        await GetErrorMessageAsync(response, "Unable to update agent tools."),
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
                    "An unexpected error occurred while updating agent tools.");
            }
        }

        public Task<ServiceResult<List<AgentModelProfileDto>>> GetModelProfilesAsync(
            CancellationToken cancellationToken = default) =>
            GetAsync<List<AgentModelProfileDto>>(
                "api/agent/model-profiles",
                "model profiles",
                cancellationToken);

        public Task<ServiceResult<AgentUsageSummaryDto>> GetUsageAsync(
            CancellationToken cancellationToken = default) =>
            GetAsync<AgentUsageSummaryDto>(
                "api/agent/usage",
                "agent usage",
                cancellationToken);

        private async Task<ServiceResult<T>> GetAsync<T>(
            string url,
            string what,
            CancellationToken cancellationToken)
        {
            try
            {
                using var response = await _httpClient.GetAsync(url, cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return ServiceResult<T>.Fail(
                        await GetErrorMessageAsync(response, $"Unable to load {what}."),
                        (int)response.StatusCode);

                var data = await response.Content.ReadFromJsonAsync<T>(
                    cancellationToken: cancellationToken);

                return data is null
                    ? ServiceResult<T>.Fail(
                        $"The API returned an invalid {what} response.",
                        (int)HttpStatusCode.InternalServerError)
                    : ServiceResult<T>.Ok(data);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                return ServiceResult<T>.Fail("Unable to connect to the API.");
            }
            catch (Exception)
            {
                return ServiceResult<T>.Fail(
                    $"An unexpected error occurred while loading {what}.");
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
