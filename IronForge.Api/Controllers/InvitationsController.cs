using IronForge.Api.Helpers;
using IronForge.Application.Tenants.Invitations;
using IronForge.Shared.Models.Tenants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronForge.Api.Controllers
{
    /// <summary>
    /// Anonymous: the invitee previews and accepts an invitation. The token
    /// is sent in the body so it does not end up in URLs and access logs.
    /// This is the only way accounts are created.
    /// </summary>
    [ApiController]
    [Route("api/invitations")]
    [AllowAnonymous]
    public sealed class InvitationsController : ControllerBase
    {
        private readonly ITenantInvitationService _invitations;

        public InvitationsController(ITenantInvitationService invitations)
        {
            _invitations = invitations;
        }

        [HttpPost("preview")]
        public async Task<IActionResult> Preview(
            InvitationTokenRequest request,
            CancellationToken cancellationToken)
        {
            return this.ToActionResult(
                await _invitations.PreviewAsync(
                    request.Token,
                    cancellationToken));
        }

        [HttpPost("accept")]
        public async Task<IActionResult> Accept(
            AcceptInvitationRequest request,
            CancellationToken cancellationToken)
        {
            return this.ToActionResult(
                await _invitations.AcceptAsync(
                    request,
                    cancellationToken));
        }
    }
}
