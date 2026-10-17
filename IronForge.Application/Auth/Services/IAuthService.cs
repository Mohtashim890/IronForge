using IronForge.Application.Auth.Models;
﻿using IronForge.Application.Auth.DTOs;

namespace IronForge.Application.Auth.Services
{
    public interface IAuthService
    {
        Task<LoginResponse?> LoginAsync(LoginRequest request);

        /// <summary>Like <see cref="LoginAsync"/>, with the reason when access is refused.</summary>
        Task<SignInOutcome> SignInAsync(LoginRequest request);
        Task<LoginResponse?> RefreshTokenAsync(
            string refreshToken);
        Task<LoginResponse?> SelectTenantAsync(
            int userId,
            int tenantId,
            string refreshToken,
            CancellationToken cancellationToken = default);
    }
}
