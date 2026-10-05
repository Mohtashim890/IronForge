using IronForge.Shared.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Shared.Services
{
    public interface IAuthService
    {
        event Action? AuthenticationStateChanged;
        Task<ServiceResult<LoginResponse>> LoginAsync(
        string username,
        string password);
        Task<ServiceResult<List<TenantMembership>>> GetMyTenantsAsync();
        Task<ServiceResult<TenantSelectionResponse>> SelectTenantAsync(int tenantId);
        Task<ActiveTenant?> GetActiveTenantAsync();
        Task<ServiceResult<LoginResponse>> RefreshTokenAsync(string? failedAccessToken = null);
        Task LogoutAsync();
        Task<string?> GetAccessTokenAsync();
        Task<bool> IsAuthenticatedAsync();
    }
}
