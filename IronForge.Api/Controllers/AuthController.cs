using IronForge.Api.Helpers;
using IronForge.Application.Auth.DTOs;
using IronForge.Application.Auth.Services;
using Microsoft.AspNetCore.Mvc;

namespace IronForge.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(
            LoginRequest request)
        {
            var result =
                await _authService.LoginAsync(request);

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

        // There is no public registration: accounts are created by accepting
        // a tenant invitation (InvitationsController).
    }
}
