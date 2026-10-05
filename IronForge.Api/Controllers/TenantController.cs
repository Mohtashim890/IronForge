using IronForge.Application.Auth.Services;
using IronForge.Application.Execution;
using IronForge.Application.Tenants.DTOs;
using IronForge.Application.Tenants.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronForge.Api.Controllers
{
    [ApiController]
    [Route("api/tenants")]
    [Authorize]
    public class TenantsController : ControllerBase
    {
        private readonly ITenantService _tenantService;
        private readonly IAuthService _authService;
        private readonly IExecutionContextAccessor _executionContext;

        public TenantsController(ITenantService tenantService, IAuthService authService, IExecutionContextAccessor executionContext)
        {
            _tenantService = tenantService;
            _authService = authService;
            _executionContext = executionContext;
        }

        [HttpGet]
        public async Task<IActionResult> GetMyTenants(CancellationToken cancellationToken)
        {
            var tenants = await _tenantService.GetMyTenantsAsync(
                cancellationToken);

            return Ok(tenants);
        }

        [HttpPost("select")]
        public async Task<IActionResult> SelectTenant(
         SelectTenantRequest request,
         CancellationToken cancellationToken)
        {
            var result = await _tenantService.SelectAsync(
                request.TenantId,
                cancellationToken);

            if (!result.Succeeded)
            {
                return Forbid();
            }

            var context = _executionContext.Current;

            if (context == null ||
                !context.Actor.UserId.HasValue)
            {
                return Unauthorized();
            }

            var authenticationResult =
                await _authService.SelectTenantAsync(
                    context.Actor.UserId.Value,
                    result.TenantId!.Value,
                    request.RefreshToken,
                    cancellationToken);

            if (authenticationResult == null)
            {
                return Unauthorized();
            }

            return Ok(new
            {
                authenticationResult.AccessToken,
                authenticationResult.RefreshToken,
                tenantId = result.TenantId,
                tenantName = result.TenantName,
                role = result.Role
            });
        }
    }
}
