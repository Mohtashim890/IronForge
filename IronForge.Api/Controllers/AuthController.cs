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

        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterRequest request)
        {
            var registered =
                await _authService.RegisterAsync(request);

            if (!registered)
            {
                return Conflict(
                    new ApiErrorResponse
                    {
                        Status = 409,
                        Message =
                            "Username or email is already registered."
                    });
            }

            return StatusCode(
                StatusCodes.Status201Created);
        }

    }
}
