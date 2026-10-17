using IronForge.Application.Auth.DTOs;

namespace IronForge.Application.Auth.Services
{
    public interface IAuthService
    {
        Task<LoginResponse?> LoginAsync(LoginRequest request);
        Task<LoginResponse?> RefreshTokenAsync(
            string refreshToken);
        Task<LoginResponse?> SelectTenantAsync(
            int userId,
            int tenantId,
            string refreshToken,
            CancellationToken cancellationToken = default);
    }
}
