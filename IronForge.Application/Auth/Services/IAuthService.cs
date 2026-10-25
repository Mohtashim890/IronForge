using IronForge.Application.Auth.Models;
﻿using IronForge.Application.Auth.DTOs;

namespace IronForge.Application.Auth.Services
{
    public interface IAuthService
    {
        Task<LoginResponse?> LoginAsync(LoginRequest request);

        /// <summary>Like <see cref="LoginAsync"/>, with the reason when access is refused.</summary>
        Task<SignInOutcome> SignInAsync(LoginRequest request);

        /// <summary>Second sign-in step: the authenticator or recovery code (or the first code while setting it up).</summary>
        Task<SignInOutcome> CompleteTwoFactorSignInAsync(
            IronForge.Shared.Models.TwoFactorSignInRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>Starts the authenticator setup for an account that must have one to sign in.</summary>
        Task<IronForge.Application.Commons.ServiceResult<IronForge.Shared.Models.TwoFactorSetupDto>> BeginTwoFactorSetupAtSignInAsync(
            string twoFactorToken,
            CancellationToken cancellationToken = default);
        Task<LoginResponse?> RefreshTokenAsync(
            string refreshToken);
        Task<LoginResponse?> SelectTenantAsync(
            int userId,
            int tenantId,
            string refreshToken,
            CancellationToken cancellationToken = default);
    }
}
