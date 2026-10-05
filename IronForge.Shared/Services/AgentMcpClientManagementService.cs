using System.Net.Http.Json;
using IronForge.Shared.Models;
using IronForge.Shared.Models.Agents;

namespace IronForge.Shared.Services;

public sealed class AgentMcpClientManagementService
    : IAgentMcpClientManagementService
{
    private readonly HttpClient _httpClient;

    public AgentMcpClientManagementService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ServiceResult<List<AgentMcpClientLinkDto>>> GetLinksAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return ServiceResult<List<AgentMcpClientLinkDto>>.Fail(
                "Agent ID is required.", 400);
        }

        try
        {
            var response = await _httpClient.GetAsync(
                $"api/agent/{Uri.EscapeDataString(agentId)}/mcp-clients",
                cancellationToken);

            return await ReadResultAsync<List<AgentMcpClientLinkDto>>(
                response,
                "Unable to load MCP client access.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            return ServiceResult<List<AgentMcpClientLinkDto>>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<AgentMcpClientLinkDto>> LinkAsync(
        string agentId,
        string clientId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agentId) ||
            string.IsNullOrWhiteSpace(clientId))
        {
            return ServiceResult<AgentMcpClientLinkDto>.Fail(
                "Agent ID and client ID are required.", 400);
        }

        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"api/agent/{Uri.EscapeDataString(agentId)}/mcp-clients",
                new AgentMcpClientLinkRequest
                {
                    ClientId = clientId.Trim()
                },
                cancellationToken);

            return await ReadResultAsync<AgentMcpClientLinkDto>(
                response,
                "Unable to grant MCP client access.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            return ServiceResult<AgentMcpClientLinkDto>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<bool>> UnlinkAsync(
        string agentId,
        string clientId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agentId) ||
            string.IsNullOrWhiteSpace(clientId))
        {
            return ServiceResult<bool>.Fail(
                "Agent ID and client ID are required.", 400);
        }

        try
        {
            var response = await _httpClient.DeleteAsync(
                $"api/agent/{Uri.EscapeDataString(agentId)}/mcp-clients/{Uri.EscapeDataString(clientId)}",
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return ServiceResult<bool>.Ok(true);
            }

            return ServiceResult<bool>.Fail(
                await ReadErrorAsync(
                    response,
                    "Unable to revoke MCP client access.",
                    cancellationToken),
                (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            return ServiceResult<bool>.Fail(ex.Message);
        }
    }

    private static async Task<ServiceResult<T>> ReadResultAsync<T>(
        HttpResponseMessage response,
        string fallbackMessage,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            var data = await response.Content.ReadFromJsonAsync<T>(
                cancellationToken: cancellationToken);

            return ServiceResult<T>.Ok(data!);
        }

        return ServiceResult<T>.Fail(
            await ReadErrorAsync(response, fallbackMessage, cancellationToken),
            (int)response.StatusCode);
    }

    private static async Task<string> ReadErrorAsync(
        HttpResponseMessage response,
        string fallbackMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await response.Content.ReadFromJsonAsync<ErrorResponse>(
                cancellationToken: cancellationToken);

            if (!string.IsNullOrWhiteSpace(json?.Message))
                return json.Message;
        }
        catch
        {
            // Fall through to plain-text response handling.
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(text)
            ? fallbackMessage
            : text;
    }

    private sealed class ErrorResponse
    {
        public string? Message { get; set; }
    }
}
