using IronForge.Application.Commons;
using IronForge.Application.McpClients.DTOs;
using IronForge.Application.McpClients.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronForge.Api.Controllers
{

    [ApiController]
    [Route("api/mcp-client")]
    [Authorize]
    public sealed class McpClientController : ControllerBase
    {
        private readonly IMcpClientManagementService _service;

        public McpClientController(
            IMcpClientManagementService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> GetClients(
            CancellationToken cancellationToken)
        {
            var result = await _service.GetClientsAsync(cancellationToken);

            return result.Success
                ? Ok(result.Data)
                : ToActionResult(result);
        }

        [HttpGet("{clientId}")]
        public async Task<IActionResult> GetClient(
            string clientId,
            CancellationToken cancellationToken)
        {
            var result = await _service.GetClientAsync(
                clientId,
                cancellationToken);

            return result.Success
                ? Ok(result.Data)
                : ToActionResult(result);
        }

        [HttpPost]
        public async Task<IActionResult> CreateClient(
            CreateMcpClientRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _service.CreateClientAsync(
                request,
                cancellationToken);

            if (!result.Success)
                return ToActionResult(result);

            return CreatedAtAction(
                nameof(GetClient),
                new { clientId = result.Data!.ClientId },
                result.Data);
        }

        [HttpPut("{clientId}")]
        public async Task<IActionResult> UpdateClient(
            string clientId,
            UpdateMcpClientRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _service.UpdateClientAsync(
                clientId,
                request,
                cancellationToken);

            return result.Success
                ? Ok(result.Data)
                : ToActionResult(result);
        }

        [HttpDelete("{clientId}")]
        public async Task<IActionResult> DeleteClient(
            string clientId,
            CancellationToken cancellationToken)
        {
            var result = await _service.DeleteClientAsync(
                clientId,
                cancellationToken);

            if (!result.Success)
                return ToActionResult(result);

            return NoContent();
        }

        private IActionResult ToActionResult<T>(
            ServiceResult<T> result)
        {
            return result.StatusCode switch
            {
                400 => BadRequest(result.ErrorMessage),
                404 => NotFound(result.ErrorMessage),
                409 => Conflict(result.ErrorMessage),
                401 => Unauthorized(),
                403 => Forbid(),
                _ => BadRequest(result.ErrorMessage)
            };
        }
    }

}
