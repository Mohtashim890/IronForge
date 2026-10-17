using IronForge.Application.Agents.Authorization;
using IronForge.Application.Agents.Models;
using IronForge.Application.Auth.Models;
using IronForge.Application.Auth.Services;
using IronForge.Application.Commons;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.Persistence;
using IronForge.Application.Tenants.Subscriptions;
using IronForge.Shared.Subscriptions;

namespace IronForge.Application.Gym.Common;

/// <summary>
/// Authorization shared by every gym service. A call is allowed when a gym
/// (tenant) is selected and the caller holds the permission. For an agent
/// the rule is the platform's: an internal agent needs the permission itself
/// (from its tool bindings) and never acts with more authority than the
/// signed-in user; an external agent needs user ∩ agent ∩ delegation.
/// </summary>
public interface IGymAccessService
{
    int? TenantId { get; }

    int? UserId { get; }

    string? Username { get; }

    Task<ResourceAuthorizationResult> AuthorizeAsync(
        string permission,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Like <see cref="AuthorizeAsync(string, CancellationToken)"/>, and the
    /// gym's subscription must include <paramref name="module"/>.
    /// </summary>
    Task<ResourceAuthorizationResult> AuthorizeAsync(
        string permission,
        string module,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The signed-in member's own profile in the selected gym, after checking
    /// <paramref name="permission"/>. Self-service operations never take a
    /// member id from the caller; they always resolve it here.
    /// </summary>
    Task<ServiceResult<MemberProfile>> GetCurrentMemberAsync(
        string permission,
        bool tracked = false,
        CancellationToken cancellationToken = default,
        string? module = null);
}

public sealed class GymAccessService : IGymAccessService
{
    private readonly IExecutionContextAccessor _executionContext;
    private readonly IUserAuthorizationService _userAuthorization;
    private readonly IAgentAuthorizationService _agentAuthorization;
    private readonly IEffectiveAgentAuthorizationService _effectiveAgentAuthorization;
    private readonly IRepository<MemberProfile> _members;
    private readonly ITenantEntitlementService _entitlements;

    public GymAccessService(
        IExecutionContextAccessor executionContext,
        IUserAuthorizationService userAuthorization,
        IAgentAuthorizationService agentAuthorization,
        IEffectiveAgentAuthorizationService effectiveAgentAuthorization,
        IRepository<MemberProfile> members,
        ITenantEntitlementService entitlements)
    {
        _executionContext = executionContext;
        _userAuthorization = userAuthorization;
        _agentAuthorization = agentAuthorization;
        _effectiveAgentAuthorization = effectiveAgentAuthorization;
        _members = members;
        _entitlements = entitlements;
    }

    public int? TenantId => _executionContext.Current?.TenantId;

    public int? UserId => _executionContext.Current?.Actor.UserId;

    public string? Username => _executionContext.Current?.Actor.Username;

    public async Task<ResourceAuthorizationResult> AuthorizeAsync(
        string permission,
        CancellationToken cancellationToken = default)
    {
        var context = _executionContext.Current;

        if (context is null)
        {
            return ResourceAuthorizationResult.Deny(
                "Execution context is not established.");
        }

        if (context.TenantId is null)
        {
            return ResourceAuthorizationResult.Deny(
                "A gym must be selected.");
        }

        var actor = context.Actor;

        if (actor.Agent?.Type == AgentType.External)
        {
            var result = await _effectiveAgentAuthorization.AuthorizeAsync(
                permission,
                cancellationToken);

            return result.Allowed
                ? ResourceAuthorizationResult.Allow()
                : ResourceAuthorizationResult.Deny(
                    result.Reason ?? "External agent is not authorized.");
        }

        if (actor.Agent?.Type == AgentType.Internal &&
            !await _agentAuthorization.HasPermissionAsync(
                actor.Agent,
                permission,
                cancellationToken))
        {
            return ResourceAuthorizationResult.Deny(
                "Internal agent does not have the required permission.");
        }

        if (!actor.UserId.HasValue)
        {
            return ResourceAuthorizationResult.Deny(
                "User identity is not established.");
        }

        return _userAuthorization.HasPermission(permission)
            ? ResourceAuthorizationResult.Allow()
            : ResourceAuthorizationResult.Deny(
                "You do not have permission for this action.");
    }

    public async Task<ResourceAuthorizationResult> AuthorizeAsync(
        string permission,
        string module,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeAsync(permission, cancellationToken);

        if (!authorization.Allowed)
        {
            return authorization;
        }

        return await _entitlements.IsModuleEnabledAsync(module, cancellationToken)
            ? authorization
            : ResourceAuthorizationResult.Deny(
                $"Your gym's subscription does not include {PlatformModules.Find(module)?.Name ?? module}.");
    }

    public async Task<ServiceResult<MemberProfile>> GetCurrentMemberAsync(
        string permission,
        bool tracked = false,
        CancellationToken cancellationToken = default,
        string? module = null)
    {
        var authorization = module is null
            ? await AuthorizeAsync(permission, cancellationToken)
            : await AuthorizeAsync(permission, module, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<MemberProfile>(authorization);
        }

        var userId = UserId!.Value;
        var tenantId = TenantId!.Value;

        var member = tracked
            ? await _members.FirstOrDefaultTrackedAsync(
                x => x.TenantId == tenantId && x.UserId == userId,
                cancellationToken)
            : await _members.FirstOrDefaultAsync(
                x => x.TenantId == tenantId && x.UserId == userId,
                cancellationToken);

        return member is null || member.AnonymizedAtUtc != null
            ? ServiceResult<MemberProfile>.Fail(
                "You do not have a membership profile at this gym.",
                404)
            : ServiceResult<MemberProfile>.Ok(member);
    }
}

public static class GymResults
{
    public static ServiceResult<T> Forbidden<T>(ResourceAuthorizationResult authorization) =>
        ServiceResult<T>.Fail(
            authorization.Reason ?? "You do not have permission for this action.",
            403);

    public static ServiceResult<T> NotFound<T>(string what) =>
        ServiceResult<T>.Fail($"{what} was not found.", 404);

    public static ServiceResult<T> Invalid<T>(string message) =>
        ServiceResult<T>.Fail(message, 400);

    public static ServiceResult<T> Conflict<T>(string message) =>
        ServiceResult<T>.Fail(message, 409);

    /// <summary>Carries a failure of another type through unchanged.</summary>
    public static ServiceResult<T> From<T, TOther>(ServiceResult<TOther> failure) =>
        ServiceResult<T>.Fail(failure.ErrorMessage, failure.StatusCode);
}
