using System.Net.Http.Json;
using IronForge.Shared.Models;
using IronForge.Shared.Models.Tenants;

namespace IronForge.Shared.Services;

/// <summary>Platform operator: tenant onboarding (api/platform/tenants).</summary>
public interface IPlatformTenantService
{
    Task<ServiceResult<List<PlatformTenantDto>>> GetTenantsAsync(
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ProvisionTenantResult>> ProvisionAsync(
        ProvisionTenantRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<PlatformTenantDto>> SetStatusAsync(
        int tenantId,
        bool isActive,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<TenantInvitationLinkDto>> InviteAdminAsync(
        int tenantId,
        string email,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<PlatformTenantDto>> UpdateSubscriptionAsync(
        int tenantId,
        UpdateTenantSubscriptionRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<List<string>>> GetModelProfilesAsync(
        CancellationToken cancellationToken = default);

    Task<ServiceResult<TemplateRolloutResultDto>> RolloutTemplatesAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>Tenant admin: members and invitations of the selected tenant (api/tenant).</summary>
public interface ITenantMembersService
{
    Task<ServiceResult<List<TenantMemberDto>>> GetMembersAsync(
        CancellationToken cancellationToken = default);

    Task<ServiceResult<TenantMemberDto>> UpdateRoleAsync(
        int userId,
        string role,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<TenantMemberDto>> UpdateStatusAsync(
        int userId,
        bool isActive,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<bool>> RemoveAsync(
        int userId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<List<TenantInvitationDto>>> GetInvitationsAsync(
        CancellationToken cancellationToken = default);

    Task<ServiceResult<TenantInvitationLinkDto>> InviteAsync(
        CreateTenantInvitationRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<bool>> RevokeInvitationAsync(
        Guid invitationId,
        CancellationToken cancellationToken = default);
}

/// <summary>Anonymous: preview and accept an invitation (api/invitations).</summary>
public interface IInvitationService
{
    Task<ServiceResult<InvitationPreviewDto>> PreviewAsync(
        string token,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<AcceptInvitationResultDto>> AcceptAsync(
        AcceptInvitationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class PlatformTenantService : IPlatformTenantService
{
    private readonly HttpClient _httpClient;

    public PlatformTenantService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<ServiceResult<List<PlatformTenantDto>>> GetTenantsAsync(
        CancellationToken cancellationToken = default)
    {
        return SendAsync<List<PlatformTenantDto>>(
            () => _httpClient.GetAsync("api/platform/tenants", cancellationToken),
            "Unable to load tenants.",
            cancellationToken);
    }

    public Task<ServiceResult<ProvisionTenantResult>> ProvisionAsync(
        ProvisionTenantRequest request,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<ProvisionTenantResult>(
            () => _httpClient.PostAsJsonAsync("api/platform/tenants", request, cancellationToken),
            "Unable to create the tenant.",
            cancellationToken);
    }

    public Task<ServiceResult<PlatformTenantDto>> SetStatusAsync(
        int tenantId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<PlatformTenantDto>(
            () => _httpClient.PutAsJsonAsync(
                $"api/platform/tenants/{tenantId}/status",
                new UpdateTenantStatusRequest { IsActive = isActive },
                cancellationToken),
            "Unable to update the tenant.",
            cancellationToken);
    }

    public Task<ServiceResult<TenantInvitationLinkDto>> InviteAdminAsync(
        int tenantId,
        string email,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<TenantInvitationLinkDto>(
            () => _httpClient.PostAsJsonAsync(
                $"api/platform/tenants/{tenantId}/admin-invitations",
                new CreateAdminInvitationRequest { Email = email },
                cancellationToken),
            "Unable to create the invitation.",
            cancellationToken);
    }

    public Task<ServiceResult<PlatformTenantDto>> UpdateSubscriptionAsync(
        int tenantId,
        UpdateTenantSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<PlatformTenantDto>(
            () => _httpClient.PutAsJsonAsync(
                $"api/platform/tenants/{tenantId}/subscription",
                request,
                cancellationToken),
            "Unable to update the subscription.",
            cancellationToken);
    }

    public Task<ServiceResult<List<string>>> GetModelProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        return SendAsync<List<string>>(
            () => _httpClient.GetAsync("api/platform/tenants/model-profiles", cancellationToken),
            "Unable to load model profiles.",
            cancellationToken);
    }

    public Task<ServiceResult<TemplateRolloutResultDto>> RolloutTemplatesAsync(
        CancellationToken cancellationToken = default)
    {
        return SendAsync<TemplateRolloutResultDto>(
            () => _httpClient.PostAsync("api/platform/tenants/template-rollout", content: null, cancellationToken),
            "Unable to roll out the agent templates.",
            cancellationToken);
    }

    private static async Task<ServiceResult<T>> SendAsync<T>(
        Func<Task<HttpResponseMessage>> send,
        string fallbackMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await send();

            return await ApiResponseReader.ReadAsync<T>(
                response,
                fallbackMessage,
                cancellationToken);
        }
        catch (Exception ex)
        {
            return ServiceResult<T>.Fail(ex.Message);
        }
    }
}

public sealed class TenantMembersService : ITenantMembersService
{
    private readonly HttpClient _httpClient;

    public TenantMembersService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<ServiceResult<List<TenantMemberDto>>> GetMembersAsync(
        CancellationToken cancellationToken = default)
    {
        return SendAsync<List<TenantMemberDto>>(
            () => _httpClient.GetAsync("api/tenant/members", cancellationToken),
            "Unable to load members.",
            cancellationToken);
    }

    public Task<ServiceResult<TenantMemberDto>> UpdateRoleAsync(
        int userId,
        string role,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<TenantMemberDto>(
            () => _httpClient.PutAsJsonAsync(
                $"api/tenant/members/{userId}/role",
                new UpdateMemberRoleRequest { Role = role },
                cancellationToken),
            "Unable to change the role.",
            cancellationToken);
    }

    public Task<ServiceResult<TenantMemberDto>> UpdateStatusAsync(
        int userId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<TenantMemberDto>(
            () => _httpClient.PutAsJsonAsync(
                $"api/tenant/members/{userId}/status",
                new UpdateMemberStatusRequest { IsActive = isActive },
                cancellationToken),
            "Unable to update the member.",
            cancellationToken);
    }

    public async Task<ServiceResult<bool>> RemoveAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.DeleteAsync(
                $"api/tenant/members/{userId}",
                cancellationToken);

            return await ApiResponseReader.ReadNoContentAsync(
                response,
                "Unable to remove the member.");
        }
        catch (Exception ex)
        {
            return ServiceResult<bool>.Fail(ex.Message);
        }
    }

    public Task<ServiceResult<List<TenantInvitationDto>>> GetInvitationsAsync(
        CancellationToken cancellationToken = default)
    {
        return SendAsync<List<TenantInvitationDto>>(
            () => _httpClient.GetAsync("api/tenant/invitations", cancellationToken),
            "Unable to load invitations.",
            cancellationToken);
    }

    public Task<ServiceResult<TenantInvitationLinkDto>> InviteAsync(
        CreateTenantInvitationRequest request,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<TenantInvitationLinkDto>(
            () => _httpClient.PostAsJsonAsync("api/tenant/invitations", request, cancellationToken),
            "Unable to create the invitation.",
            cancellationToken);
    }

    public async Task<ServiceResult<bool>> RevokeInvitationAsync(
        Guid invitationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.DeleteAsync(
                $"api/tenant/invitations/{invitationId}",
                cancellationToken);

            return await ApiResponseReader.ReadNoContentAsync(
                response,
                "Unable to revoke the invitation.");
        }
        catch (Exception ex)
        {
            return ServiceResult<bool>.Fail(ex.Message);
        }
    }

    private static async Task<ServiceResult<T>> SendAsync<T>(
        Func<Task<HttpResponseMessage>> send,
        string fallbackMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await send();

            return await ApiResponseReader.ReadAsync<T>(
                response,
                fallbackMessage,
                cancellationToken);
        }
        catch (Exception ex)
        {
            return ServiceResult<T>.Fail(ex.Message);
        }
    }
}

/// <summary>Uses an HttpClient without the JWT handler: the invitee is not signed in.</summary>
public sealed class InvitationService : IInvitationService
{
    private readonly HttpClient _httpClient;

    public InvitationService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ServiceResult<InvitationPreviewDto>> PreviewAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "api/invitations/preview",
                new InvitationTokenRequest { Token = token },
                cancellationToken);

            return await ApiResponseReader.ReadAsync<InvitationPreviewDto>(
                response,
                "Unable to load the invitation.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            return ServiceResult<InvitationPreviewDto>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<AcceptInvitationResultDto>> AcceptAsync(
        AcceptInvitationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "api/invitations/accept",
                request,
                cancellationToken);

            return await ApiResponseReader.ReadAsync<AcceptInvitationResultDto>(
                response,
                "Unable to accept the invitation.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            return ServiceResult<AcceptInvitationResultDto>.Fail(ex.Message);
        }
    }
}
