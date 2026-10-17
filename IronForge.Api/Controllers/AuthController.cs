using IronForge.Api.Helpers;
using IronForge.Application.Auth.DTOs;
using IronForge.Application.Auth.Services;
using IronForge.Shared.Models.Gym;
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

            // Correct credentials, but no gym access yet (pending or declined join request).
            if (signIn.DenialReason is not null)
            {
                return StatusCode(StatusCodes.Status403Forbidden, signIn.DenialReason);
            }

            var result = signIn.Response;

            if (result == null)
            {
                return Unauthorized(
                    ApiErrorFactory.Create(
                        StatusCodes.Status401Unauthorized,
                        "Invalid username or password."));
            }

            return Ok(result);
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
