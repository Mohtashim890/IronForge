using System.Net;
using System.Net.Http.Json;
using IronForge.Shared.Models;
using IronForge.Shared.Models.McpClients;

namespace IronForge.Shared.Services;

public sealed class McpClientManagementService : IMcpClientManagementService
{
    private readonly HttpClient _httpClient;

    public McpClientManagementService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ServiceResult<List<McpClientDto>>> GetClientsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                "api/mcp-client",
                cancellationToken);

            return await ReadResultAsync<List<McpClientDto>>(
                response,
                "Unable to load MCP clients.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            return ServiceResult<List<McpClientDto>>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<McpClientDto>> GetClientAsync(
        string clientId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return ServiceResult<McpClientDto>.Fail(
                "Client ID is required.",
                400);
        }

        try
        {
            var response = await _httpClient.GetAsync(
                $"api/mcp-client/{Uri.EscapeDataString(clientId)}",
                cancellationToken);

            return await ReadResultAsync<McpClientDto>(
                response,
                "Unable to load the MCP client.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            return ServiceResult<McpClientDto>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<McpClientDto>> CreateClientAsync(
        CreateMcpClientRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                "api/mcp-client",
                request,
                cancellationToken);

            return await ReadResultAsync<McpClientDto>(
                response,
                "Unable to create the MCP client.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            return ServiceResult<McpClientDto>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<McpClientDto>> UpdateClientAsync(
        string clientId,
        UpdateMcpClientRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return ServiceResult<McpClientDto>.Fail(
                "Client ID is required.",
                400);
        }

        try
        {
            var response = await _httpClient.PutAsJsonAsync(
                $"api/mcp-client/{Uri.EscapeDataString(clientId)}",
                request,
                cancellationToken);

            return await ReadResultAsync<McpClientDto>(
                response,
                "Unable to update the MCP client.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            return ServiceResult<McpClientDto>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<bool>> DeleteClientAsync(
        string clientId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return ServiceResult<bool>.Fail(
                "Client ID is required.",
                400);
        }

        try
        {
            var response = await _httpClient.DeleteAsync(
                $"api/mcp-client/{Uri.EscapeDataString(clientId)}",
                cancellationToken);

            if (response.IsSuccessStatusCode)
                return ServiceResult<bool>.Ok(true);

            return ServiceResult<bool>.Fail(
                await ReadErrorAsync(response, "Unable to delete the MCP client."),
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

            if (data is null)
            {
                return ServiceResult<T>.Fail(
                    "The server returned an empty response.",
                    (int)response.StatusCode);
            }

            return ServiceResult<T>.Ok(data);
        }

        return ServiceResult<T>.Fail(
            await ReadErrorAsync(response, fallbackMessage),
            (int)response.StatusCode);
    }

    private static async Task<string> ReadErrorAsync(
        HttpResponseMessage response,
        string fallbackMessage)
    {
        var text = await response.Content.ReadAsStringAsync();

        if (!string.IsNullOrWhiteSpace(text))
        {
            var trimmed = text.Trim().Trim('"');

            if (!string.IsNullOrWhiteSpace(trimmed) &&
                !trimmed.StartsWith("<"))
            {
                return trimmed;
            }
        }

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Authentication is required.",
            HttpStatusCode.Forbidden => "You are not authorized to perform this operation.",
            HttpStatusCode.NotFound => "MCP client was not found.",
            HttpStatusCode.Conflict => "An MCP client with this ID already exists.",
            _ => fallbackMessage
        };
    }
}
