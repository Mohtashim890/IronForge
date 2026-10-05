using IronForge.Application.Agents.DTOs;
using IronForge.Application.Agents.Services;
using IronForge.Application.Commons;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronForge.Api.Controllers;

[ApiController]
[Route("api/agent/{agentId}/mcp-clients")]
[Authorize]
public sealed class AgentMcpClientController : ControllerBase
{
    private readonly IAgentMcpClientManagementService _service;

    public AgentMcpClientController(IAgentMcpClientManagementService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        string agentId,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetLinksAsync(
            agentId, cancellationToken);

        return result.Success ? Ok(result.Data) : ToActionResult(result);
    }

    [HttpPost]
    public async Task<IActionResult> Link(
        string agentId,
        AgentMcpClientLinkRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.LinkAsync(
            agentId, request.ClientId, cancellationToken);

        return result.Success ? Ok(result.Data) : ToActionResult(result);
    }

    [HttpDelete("{clientId}")]
    public async Task<IActionResult> Unlink(
        string agentId,
        string clientId,
        CancellationToken cancellationToken)
    {
        var result = await _service.UnlinkAsync(
            agentId, clientId, cancellationToken);

        return result.Success ? NoContent() : ToActionResult(result);
    }

    private IActionResult ToActionResult<T>(ServiceResult<T> result)
    {
        return result.StatusCode switch
        {
            400 => BadRequest(result.ErrorMessage),
            404 => NotFound(result.ErrorMessage),
            401 => Unauthorized(),
            403 => Forbid(),
            _ => BadRequest(result.ErrorMessage)
        };
    }
}
