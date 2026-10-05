using IronForge.Application.Agents.Delegations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronForge.Api.Controllers;

[ApiController]
[Route("api/agent/delegations")]
[Authorize]
public class AgentDelegationController : ControllerBase
{
    private readonly IAgentDelegationService _delegationService;
    private readonly IAgentDelegationCredentialService _credentialService;

    public AgentDelegationController(
        IAgentDelegationService delegationService,
        IAgentDelegationCredentialService credentialService)
    {
        _delegationService = delegationService;
        _credentialService = credentialService;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        CreateAgentDelegationRequest request,
        CancellationToken cancellationToken)
    {
        var delegation = await _delegationService.CreateAsync(
            request.AgentId,
            request.ClientId,
            request.Scopes,
            TimeSpan.FromMinutes(request.LifetimeMinutes),
            cancellationToken);

        return Ok(new
        {
            delegation.DelegationId,
            delegation.UserId,
            delegation.AgentId,
            delegation.ClientId,
            delegation.Scopes,
            delegation.CreatedAtUtc,
            delegation.ExpiresAtUtc,
            delegation.Revoked
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetMyDelegations(
        CancellationToken cancellationToken)
    {
        var delegations =
            await _delegationService.GetForCurrentUserAsync(cancellationToken);

        return Ok(
            delegations.Select(Map));
    }

    [HttpGet("{delegationId:guid}")]
    public async Task<IActionResult> Get(
        Guid delegationId,
        CancellationToken cancellationToken)
    {
        var delegation =
            await _delegationService.GetForCurrentUserAsync(
                delegationId,
                cancellationToken);

        return Ok(Map(delegation));
    }

    [HttpGet("{delegationId:guid}/credential")]
    public async Task<IActionResult> CreateCredential(
    Guid delegationId,
    CancellationToken cancellationToken)
    {
        var credential =
            await _credentialService.CreateCredentialAsync(
                delegationId, cancellationToken);

        return Ok(new
        {
            AccessToken = credential
        });
    }

    [HttpPost("{delegationId:guid}/establish")]
    public async Task<IActionResult> Establish(
    Guid delegationId,
    CancellationToken cancellationToken)
    {
        await _delegationService.EstablishAsync(
            delegationId,
            cancellationToken);

        return Ok(new
        {
            DelegationId = delegationId
        });
    }

    [HttpDelete("{delegationId:guid}")]
    public async Task<IActionResult> Revoke(
        Guid delegationId,
        CancellationToken cancellationToken)
    {
        await _delegationService.RevokeAsync(
            delegationId,
            cancellationToken);

        return NoContent();
    }

    private static object Map(AgentDelegation delegation)
    {
        return new
        {
            delegation.DelegationId,
            delegation.UserId,
            delegation.AgentId,
            delegation.ClientId,
            delegation.Scopes,
            delegation.CreatedAtUtc,
            delegation.ExpiresAtUtc,
            delegation.Revoked
        };
    }
}