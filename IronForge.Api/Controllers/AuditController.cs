using IronForge.Application.Auditing.DTOs;
using IronForge.Application.Auditing.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronForge.Api.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public sealed class AuditController : ControllerBase
    {
        private readonly IAuditQueryService _auditQueryService;

        public AuditController(IAuditQueryService auditQueryService)
        {
            _auditQueryService = auditQueryService;
        }

        [HttpGet]
        public async Task<IActionResult> Query(
            [FromQuery] AuditQueryRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _auditQueryService.QueryAsync(
                request,
                cancellationToken);

            if (!result.Success)
            {
                return BadRequest(result);
            }

            return Ok(result.Data);
        }
    }
}
