using IronForge.Application.Agents.Authorization;
using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Auth.Services;
using IronForge.Application.Execution;

namespace IronForge.Application.Agents.Delegations;

public class AgentDelegationService
    : IAgentDelegationService
{
    private readonly IExecutionContextAccessor _executionContext;
    private readonly IUserAuthorizationService _userAuthorization;
    private readonly IAgentPermissionRegistry _agentPermissionRegistry;
    private readonly IAgentDelegationStore _store;
    private readonly IAuditService _auditService;
    private readonly IAgentMcpClientAuthorizationService _agentMcpClientAuthorizationService;

    public AgentDelegationService(
        IExecutionContextAccessor executionContext,
        IUserAuthorizationService userAuthorization,
        IAgentPermissionRegistry agentPermissionRegistry,
        IAgentDelegationStore store,
        IAuditService auditService,
        IAgentMcpClientAuthorizationService agentMcpClientAuthorizationService)
    {
        _executionContext = executionContext;
        _userAuthorization = userAuthorization;
        _agentPermissionRegistry = agentPermissionRegistry;
        _store = store;
        _auditService = auditService;
        _agentMcpClientAuthorizationService = agentMcpClientAuthorizationService;
    }
    public async Task<AgentDelegation> ValidateAsync(
    Guid delegationId,
    CancellationToken cancellationToken = default)
    {
        var delegation =
            await _store.GetAsync(
                delegationId,
                cancellationToken);

        if (delegation is null)
        {
            throw new UnauthorizedAccessException(
                "Delegation is invalid.");
        }

        if (delegation.Revoked)
        {
            throw new UnauthorizedAccessException(
                "Delegation has been revoked.");
        }

        if (delegation.ExpiresAtUtc <= DateTime.UtcNow)
        {
            throw new UnauthorizedAccessException(
                "Delegation has expired.");
        }

        return delegation;
    }

    public async Task<IReadOnlyList<AgentDelegation>> GetForCurrentUserAsync(
            CancellationToken cancellationToken = default)
    {
        var actor = _executionContext.Current.Actor;

        if (actor.UserId is null)
        {
            throw new UnauthorizedAccessException(
                "An authenticated user is required.");
        }

        return await _store.GetForUserAsync(
            actor.UserId.Value,
            cancellationToken);
    }

    public async Task<AgentDelegation> GetForCurrentUserAsync(
    Guid delegationId,
    CancellationToken cancellationToken = default)
    {
        var delegation =
            await _store.GetAsync(
                delegationId,
                cancellationToken);

        if (delegation is null)
        {
            throw new KeyNotFoundException(
                "Delegation was not found.");
        }

        var actor =
            _executionContext.Current.Actor;

        if (actor.UserId is null)
        {
            throw new UnauthorizedAccessException(
                "An authenticated user is required.");
        }

        if (delegation.UserId != actor.UserId.Value)
        {
            throw new UnauthorizedAccessException(
                "The delegation does not belong to the current user.");
        }

        return delegation;
    }

    public async Task EstablishAsync(
    Guid delegationId,
    CancellationToken cancellationToken = default)
    {
        var delegation =
            await GetForCurrentUserAsync(
                delegationId,
                cancellationToken);

        if (delegation.Revoked)
        {
            throw new UnauthorizedAccessException(
                "The delegation has been revoked.");
        }

        if (delegation.ExpiresAtUtc <= DateTime.UtcNow)
        {
            throw new UnauthorizedAccessException(
                "The delegation has expired.");
        }

        var actor = _executionContext.Current.Actor;

        actor.DelegationId = delegation.DelegationId;
    }

    public async Task<AgentDelegation> CreateAsync(
    string agentId,
    string clientId,
    IEnumerable<string> scopes,
    TimeSpan lifetime,
    CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException(
                "Agent ID is required.",
                nameof(agentId));
        }

        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Delegation lifetime must be greater than zero.",
                nameof(lifetime));
        }

        if (lifetime > TimeSpan.FromHours(1))
        {
            throw new ArgumentException(
                "Delegation lifetime cannot exceed one hour.",
                nameof(lifetime));
        }

        var actor =
            _executionContext.Current.Actor;

        if (actor.UserId is null)
        {
            throw new UnauthorizedAccessException(
                "An authenticated user is required to create a delegation.");
        }

        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException(
                "MCP client ID is required.",
                nameof(clientId));
        }

        var requestedScopes =
            scopes
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (requestedScopes.Count == 0)
        {
            throw new ArgumentException(
                "At least one delegation scope is required.",
                nameof(scopes));
        }

        var agentPermissions =
           await _agentPermissionRegistry
                .GetPermissionsAsync(agentId);

        foreach (var scope in requestedScopes)
        {
            if (!agentPermissions.Contains(scope))
            {
                throw new UnauthorizedAccessException(
                    $"Agent '{agentId}' is not allowed to use permission '{scope}'.");
            }

            if (!_userAuthorization.HasPermission(scope))
            {
                throw new UnauthorizedAccessException(
                    $"The current user does not have permission '{scope}'.");
            }
        }

        var clientAuthorized =
            await _agentMcpClientAuthorizationService.IsAuthorizedAsync(
                agentId,
                clientId,
                cancellationToken);

        if (!clientAuthorized)
        {
            throw new UnauthorizedAccessException(
                $"MCP client '{clientId}' is not authorized to use agent '{agentId}'.");
        }

        var now = DateTime.UtcNow;

        var delegation = new AgentDelegation
        {
            DelegationId = Guid.NewGuid(),
            UserId = actor.UserId.Value,
            AgentId = agentId,
            Scopes = requestedScopes,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(lifetime),
            ClientId = clientId.Trim(),
        };

        await _store.SaveAsync(
            delegation,
            cancellationToken);

        //We'll treat the successful security state transitions as the primary delegation audit trail,
        //while ProductService already captures resource-level denied operations.

        await _auditService.RecordAsync(
            AuditCategory.Delegation,
            "DelegationCreated",
            AuditOutcome.Succeeded,
            actorType: AuditActorType.User,
            resourceType: "Delegation",
            resourceId: delegation.DelegationId.ToString());

        return delegation;
    }

    public Task<AgentDelegation?> GetAsync(
        Guid delegationId,
        CancellationToken cancellationToken = default)
    {
        return _store.GetAsync(
            delegationId,
            cancellationToken);
    }

    public async Task RevokeAsync(
        Guid delegationId,
        CancellationToken cancellationToken = default)
    {
        var delegation=
            await _store.RevokeAsync(
                delegationId,
                cancellationToken);

        await _auditService.RecordAsync(
            AuditCategory.Delegation,
            "DelegationRevoked",
            AuditOutcome.Succeeded,
            actorType: AuditActorType.User,
            resourceType: "Delegation",
            resourceId: delegation.ToString());
    }
}