using System.Collections.Concurrent;
using System.Security.Claims;
using IronForge.Application.Auth.Models;
using IronForge.Application.Entities;
using IronForge.Application.Persistence;
using IronForge.Shared.Authorization;

namespace IronForge.Application.Auth.Services;

/// <summary>
/// Re-checks an access token against the database on every request, so a
/// deactivated or removed staff member, a changed role, a suspended gym or
/// a password reset takes effect within <see cref="SessionStateCache.Lifetime"/>
/// instead of when the token expires. The client then refreshes, which
/// either issues a token with the current role or ends the session.
/// </summary>
public interface ISessionValidator
{
    /// <summary>Null when the token still matches the account; otherwise why it does not.</summary>
    Task<string?> ValidateAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
}

public sealed record SessionState(
    bool UserActive,
    string SecurityStamp,
    string AccountRole,
    bool MembershipActive,
    string? MembershipRole,
    bool TenantHasAccess);

/// <summary>
/// A short per-instance cache of <see cref="SessionState"/>, so the check
/// costs a few queries per user every half minute rather than per request.
/// Changes made on this instance invalidate it at once.
/// </summary>
public sealed class SessionStateCache
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<(int UserId, int? TenantId), (SessionState State, DateTime ExpiresUtc)> _entries = new();

    public SessionState? Get(int userId, int? tenantId, DateTime nowUtc) =>
        _entries.TryGetValue((userId, tenantId), out var entry) && entry.ExpiresUtc > nowUtc
            ? entry.State
            : null;

    public void Set(int userId, int? tenantId, SessionState state, DateTime nowUtc)
    {
        if (_entries.Count > 10_000)
        {
            foreach (var expired in _entries.Where(x => x.Value.ExpiresUtc <= nowUtc).Select(x => x.Key).ToList())
            {
                _entries.TryRemove(expired, out _);
            }
        }

        _entries[(userId, tenantId)] = (state, nowUtc + Lifetime);
    }

    public void Invalidate(int userId)
    {
        foreach (var key in _entries.Keys.Where(x => x.UserId == userId).ToList())
        {
            _entries.TryRemove(key, out _);
        }
    }
}

public sealed class SessionValidator : ISessionValidator
{
    private readonly IRepository<User> _users;
    private readonly IRepository<TenantMembership> _memberships;
    private readonly IRepository<Tenant> _tenants;
    private readonly SessionStateCache _cache;
    private readonly TimeProvider _clock;

    public SessionValidator(
        IRepository<User> users,
        IRepository<TenantMembership> memberships,
        IRepository<Tenant> tenants,
        SessionStateCache cache,
        TimeProvider clock)
    {
        _users = users;
        _memberships = memberships;
        _tenants = tenants;
        _cache = cache;
        _clock = clock;
    }

    public async Task<string?> ValidateAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return "The token has no user.";
        }

        int? tenantId = int.TryParse(principal.FindFirstValue(CustomClaimTypes.TenantId), out var parsedTenantId)
            ? parsedTenantId
            : null;

        var now = _clock.GetUtcNow().UtcDateTime;
        var state = _cache.Get(userId, tenantId, now);

        if (state is null)
        {
            state = await LoadAsync(userId, tenantId, now, cancellationToken);
            _cache.Set(userId, tenantId, state, now);
        }

        if (!state.UserActive)
        {
            return "The account is inactive.";
        }

        if (principal.FindFirstValue(CustomClaimTypes.SecurityStamp) != state.SecurityStamp)
        {
            return "The session ended (password or two-factor reset).";
        }

        if (principal.HasClaim(CustomClaimTypes.Permission, Permissions.PlatformTenantsManage) &&
            state.AccountRole != PlatformRoles.PlatformAdmin)
        {
            return "Platform access was removed.";
        }

        if (tenantId is null)
        {
            return null;
        }

        if (!state.MembershipActive)
        {
            return "The gym membership is inactive.";
        }

        if (!string.Equals(principal.FindFirstValue(ClaimTypes.Role), state.MembershipRole, StringComparison.Ordinal))
        {
            return "The role in the gym changed.";
        }

        return state.TenantHasAccess ? null : "The gym has no access to the platform.";
    }

    private async Task<SessionState> LoadAsync(int userId, int? tenantId, DateTime now, CancellationToken cancellationToken)
    {
        var user = await _users.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (user is null)
        {
            return new SessionState(false, "", "", false, null, false);
        }

        if (tenantId is null)
        {
            return new SessionState(user.IsActive, user.SecurityStamp, user.Role, false, null, false);
        }

        var membership = await _memberships.FirstOrDefaultAsync(
            x => x.UserId == userId && x.TenantId == tenantId.Value,
            cancellationToken);

        var tenant = await _tenants.FirstOrDefaultAsync(x => x.Id == tenantId.Value, cancellationToken);

        return new SessionState(
            user.IsActive,
            user.SecurityStamp,
            user.Role,
            membership?.IsActive == true,
            membership?.Role,
            tenant?.HasAccess(now) == true);
    }
}
