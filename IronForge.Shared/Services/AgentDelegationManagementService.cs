using System.Net;
using System.Net.Http.Json;
using IronForge.Shared.Models;
using IronForge.Shared.Models.Delegations;

namespace IronForge.Shared.Services;

public sealed class AgentDelegationManagementService
    : IAgentDelegationManagementService
{
    private readonly HttpClient _httpClient;

    public AgentDelegationManagementService(
        HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ServiceResult<List<AgentDelegationDto>>> GetMyDelegationsAsync(
            CancellationToken cancellationToken = default)
    {
        try
        {
            var response =
                await _httpClient.GetAsync(
                    "api/agent/delegations",
                    cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return ServiceResult<List<AgentDelegationDto>>.Fail(
                    await ReadErrorAsync(response),
                    (int)response.StatusCode);
            }

            var data =
                await response.Content.ReadFromJsonAsync<
                    List<AgentDelegationDto>>(
                    cancellationToken: cancellationToken);

            return ServiceResult<List<AgentDelegationDto>>.Ok(
                data ?? []);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ServiceResult<List<AgentDelegationDto>>.Fail(
                $"Unable to load delegations: {ex.Message}");
        }
    }

    public async Task<ServiceResult<AgentDelegationDto>> GetDelegationAsync(
            Guid delegationId,
            CancellationToken cancellationToken = default)
    {
        try
        {
            var response =
                await _httpClient.GetAsync(
                    $"api/agent/delegations/{delegationId}",
                    cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return ServiceResult<AgentDelegationDto>.Fail(
                    await ReadErrorAsync(response),
                    (int)response.StatusCode);
            }

            var data =
                await response.Content.ReadFromJsonAsync<
                    AgentDelegationDto>(
                    cancellationToken: cancellationToken);

            return data is null
                ? ServiceResult<AgentDelegationDto>.Fail(
                    "The server returned an invalid delegation.")
                : ServiceResult<AgentDelegationDto>.Ok(data);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ServiceResult<AgentDelegationDto>.Fail(
                $"Unable to load delegation: {ex.Message}");
        }
    }

    public async Task<ServiceResult<AgentDelegationDto>> CreateDelegationAsync(
            CreateAgentDelegationRequest request,
            CancellationToken cancellationToken = default)
    {
        try
        {
            var response =
                await _httpClient.PostAsJsonAsync(
                    "api/agent/delegations",
                    request,
                    cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return ServiceResult<AgentDelegationDto>.Fail(
                    await ReadErrorAsync(response),
                    (int)response.StatusCode);
            }

            var data =
                await response.Content.ReadFromJsonAsync<
                    AgentDelegationDto>(
                    cancellationToken: cancellationToken);

            return data is null
                ? ServiceResult<AgentDelegationDto>.Fail(
                    "The server returned an invalid delegation.")
                : ServiceResult<AgentDelegationDto>.Ok(data);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ServiceResult<AgentDelegationDto>.Fail(
                $"Unable to create delegation: {ex.Message}");
        }
    }

    public async Task<ServiceResult<bool>> RevokeDelegationAsync(
            Guid delegationId,
            CancellationToken cancellationToken = default)
    {
        try
        {
            var response =
                await _httpClient.DeleteAsync(
                    $"api/agent/delegations/{delegationId}",
                    cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return ServiceResult<bool>.Fail(
                    await ReadErrorAsync(response),
                    (int)response.StatusCode);
            }

            return ServiceResult<bool>.Ok(true);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ServiceResult<bool>.Fail(
                $"Unable to revoke delegation: {ex.Message}");
        }
    }

    private static async Task<string> ReadErrorAsync(
        HttpResponseMessage response)
    {
        var body =
            await response.Content.ReadAsStringAsync();

        return string.IsNullOrWhiteSpace(body)
            ? $"Request failed with HTTP {(int)response.StatusCode}."
            : body;
    }

    public async Task<ServiceResult<string>> GetDelegationCredentialsAsync(Guid delegationId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response =
                await _httpClient.GetAsync(
                    $"api/agent/delegations/{delegationId}/credential",
                    cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return ServiceResult<string>.Fail(
                    await ReadErrorAsync(response),
                    (int)response.StatusCode);
            }

            var data =
                await response.Content.ReadFromJsonAsync<DelegationCredentialResponse>(
                    cancellationToken: cancellationToken);

            return data is null
                ? ServiceResult<string>.Fail(
                    "The server returned an invalid delegation.")
                : ServiceResult<string>.Ok(data.AccessToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ServiceResult<string>.Fail(
                $"Unable to create delegation: {ex.Message}");
        }
    }
    private sealed class DelegationCredentialResponse
    {
        public string AccessToken { get; set; } = string.Empty;
    }
}
