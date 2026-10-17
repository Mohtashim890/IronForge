using IronForge.Api.Helpers;
using IronForge.Application.Tenants.Services;
using IronForge.Shared.Models.Tenants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronForge.Api.Controllers
{
    /// <summary>
    /// Read-only platform views for operators (the platform app). Tenant
    /// changes stay in <see cref="PlatformTenantsController"/>.
    /// </summary>
    [ApiController]
    [Route("api/platform")]
    [Authorize]
    public sealed class PlatformController : ControllerBase
    {
        private readonly IPlatformInsightsService _insights;

        public PlatformController(IPlatformInsightsService insights)
        {
            _insights = insights;
        }

        [HttpGet("overview")]
        public async Task<IActionResult> Overview(CancellationToken cancellationToken) =>
            this.ToActionResult(await _insights.GetOverviewAsync(cancellationToken));

        [HttpGet("tenants/{tenantId:int}")]
        public async Task<IActionResult> Tenant(int tenantId, CancellationToken cancellationToken) =>
            this.ToActionResult(await _insights.GetTenantAsync(tenantId, cancellationToken));

        [HttpGet("catalog")]
        public async Task<IActionResult> Catalog(CancellationToken cancellationToken) =>
            this.ToActionResult(await _insights.GetCatalogAsync(cancellationToken));

        [HttpGet("audit")]
        public async Task<IActionResult> Audit([FromQuery] PlatformAuditQuery query, CancellationToken cancellationToken) =>
            this.ToActionResult(await _insights.GetAuditAsync(query, cancellationToken));
    }
}
