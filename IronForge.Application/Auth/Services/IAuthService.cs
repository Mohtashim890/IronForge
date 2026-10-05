using IronForge.Application.Auth.DTOs;

namespace IronForge.Application.Auth.Services
{
    public interface IAuthService
    {
        Task<LoginResponse?> LoginAsync(LoginRequest request);
        Task<LoginResponse?> RefreshTokenAsync(
            string refreshToken);
        Task<bool> RegisterAsync(RegisterRequest request);
        Task<string> CreateTenantAccessTokenAsync(
            int userId,
            int tenantId,
            CancellationToken cancellationToken = default);
        Task<LoginResponse?> SelectTenantAsync(
            int userId,
            int tenantId,
            string refreshToken,
            CancellationToken cancellationToken = default);
    }
}
