using System.Text;
using IronForge.Api.Helpers;
using IronForge.Application.Gym.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IronForge.Api.Controllers
{
    /// <summary>
    /// The ZKTeco push protocol (ADMS, "iclock"). The paths are fixed by the
    /// terminal firmware: point the terminal's Cloud Server / ADMS setting at
    /// the API host. A terminal identifies itself by its serial number (SN),
    /// which an admin registered under Check-in devices; the answers are plain
    /// text. Every rule lives in <see cref="ITerminalProtocolService"/>.
    /// </summary>
    [ApiController]
    [Route("iclock")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Device)]
    public sealed class ZkTecoController : ControllerBase
    {
        private readonly ITerminalProtocolService _terminal;

        public ZkTecoController(ITerminalProtocolService terminal) => _terminal = terminal;

        /// <summary>The terminal starts (options=all) or checks in; answers its options.</summary>
        [HttpGet("cdata")]
        public Task<IActionResult> Connect([FromQuery(Name = "SN")] string? serial, CancellationToken cancellationToken) =>
            RunAsync(serial, () => _terminal.ConnectAsync(cancellationToken), cancellationToken);

        /// <summary>Uploads: table=ATTLOG (entries), OPERLOG or USERINFO (users and fingerprints).</summary>
        [HttpPost("cdata")]
        public async Task<IActionResult> Upload(
            [FromQuery(Name = "SN")] string? serial,
            [FromQuery] string? table,
            CancellationToken cancellationToken)
        {
            var body = await ReadBodyAsync(cancellationToken);
            return await RunAsync(serial, () => _terminal.UploadAsync(table, body, cancellationToken), cancellationToken);
        }

        /// <summary>The terminal polls for commands.</summary>
        [HttpGet("getrequest")]
        public Task<IActionResult> Poll([FromQuery(Name = "SN")] string? serial, CancellationToken cancellationToken) =>
            RunAsync(serial, () => _terminal.PollAsync(cancellationToken), cancellationToken);

        /// <summary>The terminal reports the results of commands.</summary>
        [HttpPost("devicecmd")]
        public async Task<IActionResult> CommandResults([FromQuery(Name = "SN")] string? serial, CancellationToken cancellationToken)
        {
            var body = await ReadBodyAsync(cancellationToken);
            return await RunAsync(serial, () => _terminal.ReportResultsAsync(body, cancellationToken), cancellationToken);
        }

        private async Task<IActionResult> RunAsync(string? serial, Func<Task<string>> action, CancellationToken cancellationToken)
        {
            var terminal = await _terminal.AuthenticateAsync(
                serial,
                HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString(),
                cancellationToken);

            if (!terminal.Success)
            {
                return StatusCode(terminal.StatusCode ?? 401, terminal.ErrorMessage);
            }

            return Content(await action(), "text/plain", Encoding.UTF8);
        }

        /// <summary>Uploads are small text; a terminal sending more than 1 MB at once is refused.</summary>
        private async Task<string> ReadBodyAsync(CancellationToken cancellationToken)
        {
            if (Request.ContentLength > 1_048_576)
            {
                return "";
            }

            using var reader = new StreamReader(Request.Body, Encoding.UTF8);
            var body = await reader.ReadToEndAsync(cancellationToken);

            return body.Length > 1_048_576 ? "" : body;
        }
    }
}
