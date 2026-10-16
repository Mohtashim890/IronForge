using IronForge.Api.Helpers;
using IronForge.Application.Tenants.Services;
using IronForge.Shared.Models.Tenants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronForge.Api.Controllers
{
    /// <summary>
    /// Platform operator: onboard and manage client tenants. Works without a
    /// selected tenant; the service requires platform.tenants.manage.
    /// </summary>
    [ApiController]
    [Route("api/platform/tenants")]
    [Authorize]
    public sealed class PlatformTenantsController : ControllerBase
    {
        private readonly ITenantProvisioningService _service;
        private readonly IAgentTemplateRolloutService _templates;

        public PlatformTenantsController(
            ITenantProvisioningService service,
            IAgentTemplateRolloutService templates)
        {
            _service = service;
            _templates = templates;
        }

        [HttpGet]
        public async Task<IActionResult> GetTenants(
            CancellationToken cancellationToken)
        {
            return this.ToActionResult(
                await _service.ListTenantsAsync(cancellationToken));
        }

        /// <summary>Updates managed template agents in every tenant.</summary>
        [HttpPost("template-rollout")]
        public async Task<IActionResult> RolloutTemplates(
            CancellationToken cancellationToken)
        {
            return this.ToActionResult(
                await _templates.RolloutAsync(cancellationToken));
        }

        [HttpGet("model-profiles")]
        public IActionResult GetModelProfiles()
        {
            return this.ToActionResult(_service.ListModelProfiles());
        }

        [HttpPost]
        public async Task<IActionResult> ProvisionTenant(
            ProvisionTenantRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _service.ProvisionAsync(
                request,
                cancellationToken);

            return result.Success
                ? StatusCode(StatusCodes.Status201Created, result.Data)
                : this.ToErrorResult(result);
        }

        [HttpPut("{tenantId:int}/status")]
        public async Task<IActionResult> SetStatus(
            int tenantId,
            UpdateTenantStatusRequest request,
            CancellationToken cancellationToken)
        {
            return this.ToActionResult(
                await _service.SetStatusAsync(
                    tenantId,
                    request.IsActive,
                    cancellationToken));
        }

        [HttpPut("{tenantId:int}/subscription")]
        public async Task<IActionResult> UpdateSubscription(
            int tenantId,
            UpdateTenantSubscriptionRequest request,
            CancellationToken cancellationToken)
        {
            return this.ToActionResult(
                await _service.UpdateSubscriptionAsync(
                    tenantId,
                    request,
                    cancellationToken));
        }

        [HttpPost("{tenantId:int}/admin-invitations")]
        public async Task<IActionResult> InviteAdmin(
            int tenantId,
            CreateAdminInvitationRequest request,
            CancellationToken cancellationToken)
        {
            return this.ToActionResult(
                await _service.InviteAdminAsync(
                    tenantId,
                    request.Email,
                    cancellationToken));
        }
    }
}
