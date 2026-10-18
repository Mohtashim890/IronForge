using IronForge.Api.Helpers;
using IronForge.Application.Gym.CheckIns;
using IronForge.Application.Gym.Members;
using IronForge.Shared.CheckIns;
using IronForge.Shared.Models.Gym;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IronForge.Api.Controllers
{
    /// <summary>
    /// Unattended check-in devices. They have no user and no JWT: a device
    /// pairs once with a code from an admin, then sends its device key in
    /// <see cref="KioskHeaders.DeviceKey"/>. The key establishes the request
    /// as that device, which may only scan member passes.
    /// </summary>
    [ApiController]
    [Route("api/kiosk")]
    [AllowAnonymous]
    public sealed class KioskController : ControllerBase
    {
        private readonly IKioskService _kiosk;
        private readonly ICheckInService _checkIns;

        public KioskController(IKioskService kiosk, ICheckInService checkIns)
        {
            _kiosk = kiosk;
            _checkIns = checkIns;
        }

        [HttpPost("pair")]
        [EnableRateLimiting(RateLimitPolicies.Auth)]
        public async Task<IActionResult> Pair(PairKioskRequest request, CancellationToken cancellationToken) =>
            this.ToActionResult(await _kiosk.PairAsync(request.PairingCode, cancellationToken));

        /// <summary>Lets the device check that its key still works.</summary>
        [HttpGet("me")]
        [EnableRateLimiting(RateLimitPolicies.Device)]
        public async Task<IActionResult> Me(CancellationToken cancellationToken) =>
            this.ToActionResult(await _kiosk.AuthenticateAsync(Request.Headers[KioskHeaders.DeviceKey], cancellationToken));

        /// <summary>The entrance code to show now; the screen fetches the next one when it expires.</summary>
        [HttpGet("gym-qr")]
        [EnableRateLimiting(RateLimitPolicies.Device)]
        public async Task<IActionResult> GymQr(CancellationToken cancellationToken)
        {
            var device = await _kiosk.AuthenticateAsync(Request.Headers[KioskHeaders.DeviceKey], cancellationToken);

            if (!device.Success)
            {
                return this.ToErrorResult(device);
            }

            return this.ToActionResult(await _checkIns.GetGymQrAsync(cancellationToken));
        }

        [HttpPost("check-ins")]
        [EnableRateLimiting(RateLimitPolicies.Device)]
        public async Task<IActionResult> CheckIn(ScanPassRequest request, CancellationToken cancellationToken)
        {
            var device = await _kiosk.AuthenticateAsync(Request.Headers[KioskHeaders.DeviceKey], cancellationToken);

            if (!device.Success)
            {
                return this.ToErrorResult(device);
            }

            return this.ToActionResult(await _checkIns.ScanPassAsync(request.Code, cancellationToken));
        }
    }
}
