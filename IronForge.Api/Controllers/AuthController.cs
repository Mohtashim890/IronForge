using IronForge.Api.Helpers;
using IronForge.Application.Auth.DTOs;
using IronForge.Application.Auth.Services;
using IronForge.Shared.Models.Gym;
using TwoFactorSetupStartRequest = IronForge.Shared.Models.TwoFactorSetupStartRequest;
using TwoFactorSignInRequest = IronForge.Shared.Models.TwoFactorSignInRequest;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IronForge.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly IPasswordResetService _passwordReset;

        public AuthController(IAuthService authService, IPasswordResetService passwordReset)
        {
            _authService = authService;
            _passwordReset = passwordReset;
        }

        /// <summary>Signs in with a username or an email address.</summary>
        [HttpPost("login")]
        [EnableRateLimiting(RateLimitPolicies.Auth)]
        public async Task<IActionResult> Login(
            LoginRequest request)
        {
            var signIn =
                await _authService.SignInAsync(request);

            return SignInResult(signIn);
        }

        /// <summary>
        /// Second sign-in step: the code from the authenticator app (or a
        /// recovery code). While two-factor is being set up at sign-in, the
        /// first code confirms it and the response carries the recovery codes.
        /// </summary>
        [HttpPost("two-factor")]
        [EnableRateLimiting(RateLimitPolicies.Auth)]
        public async Task<IActionResult> TwoFactor(
            TwoFactorSignInRequest request,
            CancellationToken cancellationToken) =>
            SignInResult(await _authService.CompleteTwoFactorSignInAsync(request, cancellationToken));

        /// <summary>Starts the authenticator setup for an account that must use two-factor (gym admins, operators).</summary>
        [HttpPost("two-factor/setup")]
        [EnableRateLimiting(RateLimitPolicies.Auth)]
        public async Task<IActionResult> TwoFactorSetup(
            TwoFactorSetupStartRequest request,
            CancellationToken cancellationToken) =>
            this.ToActionResult(await _authService.BeginTwoFactorSetupAtSignInAsync(request.TwoFactorToken, cancellationToken));

        /// <summary>
        /// Tokens or a two-factor challenge (200); a refusal with its reason as
        /// plain text: 403 no gym access yet (pending or declined join request),
        /// 429 account locked, 401 wrong code; 401 wrong username or password.
        /// </summary>
        private IActionResult SignInResult(IronForge.Application.Auth.Models.SignInOutcome signIn)
        {
            if (signIn.Succeeded)
            {
                return Ok(signIn.Response);
            }

            if (signIn.DenialReason is not null)
            {
                return StatusCode(signIn.StatusCode, signIn.DenialReason);
            }

            return Unauthorized(
                ApiErrorFactory.Create(
                    StatusCodes.Status401Unauthorized,
                    "Invalid username or password."));
        }
        [HttpPost("refresh")]
        [EnableRateLimiting(RateLimitPolicies.Anonymous)]
        public async Task<IActionResult> Refresh(
    RefreshTokenRequest request)
        {
            var result =
                await _authService.RefreshTokenAsync(
                    request.RefreshToken);

            if (result == null)
            {
                return Unauthorized(
                    new ApiErrorResponse
                    {
                        Status = 401,
                        Message = "Invalid or expired refresh token."
                    });
            }

            return Ok(result);
        }

        /// <summary>
        /// Emails a reset link. Always 202, so the response does not reveal
        /// whether the email has an account.
        /// </summary>
        [HttpPost("forgot-password")]
        [EnableRateLimiting(RateLimitPolicies.Auth)]
        public async Task<IActionResult> ForgotPassword(
            ForgotPasswordRequest request,
            CancellationToken cancellationToken)
        {
            await _passwordReset.RequestAsync(request.Email, cancellationToken);

            return Accepted();
        }

        [HttpPost("reset-password")]
        [EnableRateLimiting(RateLimitPolicies.Auth)]
        public async Task<IActionResult> ResetPassword(
            ResetPasswordRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _passwordReset.ResetAsync(
                request.Token,
                request.NewPassword,
                cancellationToken);

            return result.Success ? NoContent() : this.ToErrorResult(result);
        }

        // Accounts are created by accepting an invitation (InvitationsController)
        // or by joining a gym with its code (JoinController).
    }
}
