using IronForge.Api.Helpers;
using IronForge.Application.Auth.Services;
using IronForge.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IronForge.Api.Controllers
{
    /// <summary>The signed-in person's own two-factor sign-in (with or without a gym selected).</summary>
    [ApiController]
    [Authorize]
    [Route("api/account/two-factor")]
    public sealed class AccountController : ControllerBase
    {
        private readonly ITwoFactorService _twoFactor;

        public AccountController(ITwoFactorService twoFactor) => _twoFactor = twoFactor;

        [HttpGet]
        public async Task<IActionResult> Status(CancellationToken cancellationToken) =>
            this.ToActionResult(await _twoFactor.GetMyStatusAsync(cancellationToken));

        [HttpPost("setup")]
        [EnableRateLimiting(RateLimitPolicies.Auth)]
        public async Task<IActionResult> Setup(CancellationToken cancellationToken) =>
            this.ToActionResult(await _twoFactor.BeginMySetupAsync(cancellationToken));

        [HttpPost("enable")]
        [EnableRateLimiting(RateLimitPolicies.Auth)]
        public async Task<IActionResult> Enable(TwoFactorCodeRequest request, CancellationToken cancellationToken) =>
            this.ToActionResult(await _twoFactor.EnableMineAsync(request, cancellationToken));

        [HttpPost("recovery-codes")]
        [EnableRateLimiting(RateLimitPolicies.Auth)]
        public async Task<IActionResult> RecoveryCodes(TwoFactorCodeRequest request, CancellationToken cancellationToken) =>
            this.ToActionResult(await _twoFactor.RegenerateMyRecoveryCodesAsync(request, cancellationToken));

        [HttpPost("disable")]
        [EnableRateLimiting(RateLimitPolicies.Auth)]
        public async Task<IActionResult> Disable(TwoFactorCodeRequest request, CancellationToken cancellationToken) =>
            this.ToActionResult(await _twoFactor.DisableMineAsync(request, cancellationToken));
    }
}
