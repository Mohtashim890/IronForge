using IronForge.Application.Agents;
using IronForge.Application.Agents.DTOs;
using IronForge.Application.Agents.Memory.ConversationSession;
using IronForge.Application.Agents.Memory.Summarization;
using IronForge.Application.Agents.Runtime;
using IronForge.Application.Agents.Usage;
using IronForge.Application.Agents.Services;
using IronForge.Application.Commons;
using IronForge.Shared.Models.Agents.Manage;
using AgentDto = IronForge.Application.Agents.DTOs.AgentDto;
using CreateAgentRequest = IronForge.Application.Agents.DTOs.CreateAgentRequest;
using UpdateAgentRequest = IronForge.Application.Agents.DTOs.UpdateAgentRequest;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronForge.Api.Controllers;

[ApiController]
[Route("api/agent")]
[Authorize]
public class AgentController : ControllerBase
{
    private readonly IConversationSummaryService _conversationSummaryService;
    private readonly IConversationService _conversationService;
    private readonly IAgentManagementService _agentManagementService;
    private readonly IAgentRuntime _agentRuntime;

    public AgentController(
        IConversationSummaryService conversationSummaryService,
        IConversationService conversationService,
        IAgentManagementService agentManagementService,
        IAgentRuntime agentRuntime)
    {
        _conversationSummaryService = conversationSummaryService;
        _conversationService = conversationService;
        _agentManagementService = agentManagementService;
        _agentRuntime = agentRuntime;
    }


    [HttpGet]
    public async Task<IActionResult> GetAgents(
        CancellationToken cancellationToken)
    {
        var result =
            await _agentManagementService.GetAgentsAsync(
                cancellationToken);

        return Ok(result.Data);
    }

    // Literal segments (tools, available, model-profiles, usage) take
    // precedence over the {agentId} route below.

    [HttpGet("available")]
    public async Task<IActionResult> GetAvailableAgents(
        CancellationToken cancellationToken)
    {
        var result =
            await _agentManagementService.GetAvailableAgentsAsync(
                cancellationToken);

        return result.Success
            ? Ok(result.Data)
            : ToActionResult(result);
    }

    [HttpGet("model-profiles")]
    public async Task<IActionResult> GetModelProfiles(
        CancellationToken cancellationToken)
    {
        var result =
            await _agentManagementService.GetModelProfilesAsync(
                cancellationToken);

        return result.Success
            ? Ok(result.Data)
            : ToActionResult(result);
    }

    [HttpGet("usage")]
    public async Task<IActionResult> GetUsage(
        CancellationToken cancellationToken)
    {
        var result =
            await _agentManagementService.GetUsageAsync(
                cancellationToken);

        return result.Success
            ? Ok(result.Data)
            : ToActionResult(result);
    }

    [HttpGet("tools")]
    public async Task<IActionResult> GetToolCatalog(
        CancellationToken cancellationToken)
    {
        var result =
            await _agentManagementService.GetToolCatalogAsync(
                cancellationToken);

        if (!result.Success)
        {
            return ToActionResult(result);
        }

        return Ok(result.Data);
    }

    [HttpGet("{agentId}")]
    public async Task<IActionResult> GetAgent(
        string agentId,
        CancellationToken cancellationToken)
    {
        var result =
            await _agentManagementService.GetAgentAsync(
                agentId,
                cancellationToken);

        if (!result.Success)
        {
            return ToActionResult(result);
        }

        return Ok(result.Data);
    }

    [HttpPost]
    public async Task<IActionResult> CreateAgent(
        CreateAgentRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await _agentManagementService.CreateAgentAsync(
                request,
                cancellationToken);

        if (!result.Success)
        {
            return ToActionResult(result);
        }

        return CreatedAtAction(
            nameof(GetAgent),
            new
            {
                agentId = result.Data!.AgentId
            },
            result.Data);
    }

    [HttpPut("{agentId}")]
    public async Task<IActionResult> UpdateAgent(
        string agentId,
        UpdateAgentRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await _agentManagementService.UpdateAgentAsync(
                agentId,
                request,
                cancellationToken);

        if (!result.Success)
        {
            return ToActionResult(result);
        }

        return Ok(result.Data);
    }

    [HttpDelete("{agentId}")]
    public async Task<IActionResult> DeleteAgent(
        string agentId,
        CancellationToken cancellationToken)
    {
        var result =
            await _agentManagementService.DeleteAgentAsync(
                agentId,
                cancellationToken);

        if (!result.Success)
        {
            return ToActionResult(result);
        }

        return NoContent();
    }

    [HttpPost("{agentId}/detach")]
    public async Task<IActionResult> Detach(
        string agentId,
        CancellationToken cancellationToken)
    {
        var result =
            await _agentManagementService.DetachAsync(
                agentId,
                cancellationToken);

        return result.Success
            ? Ok(result.Data)
            : ToActionResult(result);
    }

    [HttpPut("{agentId}/tools")]
    public async Task<IActionResult> UpdateTools(
        string agentId,
        UpdateAgentToolsRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await _agentManagementService.UpdateToolsAsync(
                agentId,
                request,
                cancellationToken);

        if (!result.Success)
        {
            return ToActionResult(result);
        }

        return Ok(result.Data);
    }

    // ============================================================
    // Any internal agent (generic runtime)
    // ============================================================

    [HttpPost("{agentId}/chat")]
    public async Task<IActionResult> ChatWithAgent(
        string agentId,
        AgentChatRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result =
                await _agentRuntime.RunAsync(
                    agentId,
                    request.SessionId,
                    request.Message,
                    cancellationToken);

            return Ok(result);
        }
        catch (AgentQuotaExceededException ex)
        {
            return QuotaExceeded(ex);
        }
    }

    [HttpPost("{agentId}/approvals/{approvalId}/decision")]
    public async Task<IActionResult> DecideAgentApproval(
        string agentId,
        string approvalId,
        AgentApprovalDecisionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result =
                await _agentRuntime.RespondToApprovalAsync(
                    agentId,
                    approvalId,
                    request.Approved,
                    request.Reason,
                    cancellationToken);

            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            // E.g. a High-risk action that still needs a second approver.
            return StatusCode(StatusCodes.Status403Forbidden, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    private ObjectResult QuotaExceeded(AgentQuotaExceededException exception)
    {
        Response.Headers.RetryAfter =
            ((int)Math.Ceiling((exception.ResetsAtUtc - DateTime.UtcNow).TotalSeconds))
                .ToString(System.Globalization.CultureInfo.InvariantCulture);

        return StatusCode(
            StatusCodes.Status429TooManyRequests,
            exception.Message);
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


    // ============================================================
    // Sessions
    // ============================================================

    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions(
        [FromQuery] string? agentId,
        CancellationToken cancellationToken)
    {
        var result =
            await _conversationService.GetMySessionsAsync(
                agentId,
                cancellationToken);

        return Ok(result);
    }


    [HttpPost("sessions")]
    public async Task<IActionResult> CreateSession(
        CreateAgentSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AgentId))
        {
            return BadRequest(
                new
                {
                    message = "Agent ID is required."
                });
        }

        var result =
            await _conversationService.CreateSessionAsync(
                request.AgentId,
                request.Title,
                cancellationToken);

        return Ok(result);
    }


    [HttpGet("sessions/{sessionId:guid}")]
    public async Task<IActionResult> GetSession(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var result =
            await _conversationService.GetSessionAsync(
                sessionId,
                cancellationToken);

        if (result is null)
        {
            return NotFound();
        }

        return Ok(result);
    }


    [HttpGet("sessions/{sessionId:guid}/messages")]
    public async Task<IActionResult> GetMessages(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var result =
            await _conversationService.GetMessagesAsync(
                sessionId,
                cancellationToken);

        return Ok(result);
    }


    [HttpPost("sessions/{sessionId:guid}/archive")]
    public async Task<IActionResult> ArchiveSession(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        await _conversationService.ArchiveSessionAsync(
            sessionId,
            cancellationToken);

        return NoContent();
    }


    // ============================================================
    // Conversation Summary
    // ============================================================

    [HttpPost("sessions/{sessionId:guid}/summary")]
    public async Task<IActionResult> CreateOrUpdateSummary(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var result =
            await _conversationSummaryService.CreateOrUpdateAsync(
                sessionId,
                cancellationToken);

        return Ok(result);
    }


    [HttpGet("sessions/{sessionId:guid}/summary")]
    public async Task<IActionResult> GetSummary(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var result =
            await _conversationSummaryService.GetAsync(
                sessionId,
                cancellationToken);

        return Ok(result);
    }
}