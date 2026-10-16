using IronForge.Api.Helpers;
using IronForge.Application.Tenants.Invitations;
using IronForge.Application.Tenants.Services;
using IronForge.Shared.Models.Tenants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronForge.Api.Controllers
{
    /// <summary>Members and invitations of the selected tenant.</summary>
    [ApiController]
    [Route("api/tenant")]
    [Authorize]
    public sealed class TenantMembersController : ControllerBase
    {
        private readonly ITenantMemberService _members;
        private readonly ITenantInvitationService _invitations;

        public TenantMembersController(
            ITenantMemberService members,
            ITenantInvitationService invitations)
        {
            _members = members;
            _invitations = invitations;
        }

        [HttpGet("members")]
        public async Task<IActionResult> GetMembers(
            CancellationToken cancellationToken)
        {
            return this.ToActionResult(
                await _members.ListAsync(cancellationToken));
        }

        [HttpPut("members/{userId:int}/role")]
        public async Task<IActionResult> UpdateRole(
            int userId,
            UpdateMemberRoleRequest request,
            CancellationToken cancellationToken)
        {
            return this.ToActionResult(
                await _members.UpdateRoleAsync(
                    userId,
                    request.Role,
                    cancellationToken));
        }

        [HttpPut("members/{userId:int}/status")]
        public async Task<IActionResult> UpdateStatus(
            int userId,
            UpdateMemberStatusRequest request,
            CancellationToken cancellationToken)
        {
            return this.ToActionResult(
                await _members.UpdateStatusAsync(
                    userId,
                    request.IsActive,
                    cancellationToken));
        }

        [HttpDelete("members/{userId:int}")]
        public async Task<IActionResult> RemoveMember(
            int userId,
            CancellationToken cancellationToken)
        {
            var result = await _members.RemoveAsync(
                userId,
                cancellationToken);

            return result.Success
                ? NoContent()
                : this.ToErrorResult(result);
        }

        [HttpGet("invitations")]
        public async Task<IActionResult> GetInvitations(
            CancellationToken cancellationToken)
        {
            return this.ToActionResult(
                await _invitations.ListForCurrentTenantAsync(cancellationToken));
        }

        [HttpPost("invitations")]
        public async Task<IActionResult> CreateInvitation(
            CreateTenantInvitationRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _invitations.CreateForCurrentTenantAsync(
                request,
                cancellationToken);

            return result.Success
                ? StatusCode(StatusCodes.Status201Created, result.Data)
                : this.ToErrorResult(result);
        }

        [HttpDelete("invitations/{invitationId:guid}")]
        public async Task<IActionResult> RevokeInvitation(
            Guid invitationId,
            CancellationToken cancellationToken)
        {
            var result = await _invitations.RevokeForCurrentTenantAsync(
                invitationId,
                cancellationToken);

            return result.Success
                ? NoContent()
                : this.ToErrorResult(result);
        }
    }
}
