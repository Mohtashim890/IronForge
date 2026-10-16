using System.Net.Http.Json;
using IronForge.Shared.Models;
using IronForge.Shared.Models.Agents;

namespace IronForge.Shared.Services;

/// <summary>The approvals queue for High/Critical-risk agent actions (api/approvals).</summary>
public interface IApprovalQueueClient
{
    Task<ServiceResult<ApprovalQueueDto>> GetAsync(
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ApprovalQueueItemDto>> DecideAsync(
        string approvalId,
        bool approved,
        string? reason,
        CancellationToken cancellationToken = default);
}

public sealed class ApprovalQueueClient : IApprovalQueueClient
{
    private readonly HttpClient _httpClient;

    public ApprovalQueueClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ServiceResult<ApprovalQueueDto>> GetAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync("api/approvals", cancellationToken);

            return await ApiResponseReader.ReadAsync<ApprovalQueueDto>(
                response,
                "Unable to load approvals.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            return ServiceResult<ApprovalQueueDto>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<ApprovalQueueItemDto>> DecideAsync(
        string approvalId,
        bool approved,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                $"api/approvals/{Uri.EscapeDataString(approvalId)}/decision",
                new ApprovalQueueDecisionRequest { Approved = approved, Reason = reason },
                cancellationToken);

            return await ApiResponseReader.ReadAsync<ApprovalQueueItemDto>(
                response,
                "Unable to record the decision.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            return ServiceResult<ApprovalQueueItemDto>.Fail(ex.Message);
        }
    }
}
