using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Auth.Services;
using IronForge.Application.Commons;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.Persistence;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Tenants;

namespace IronForge.Application.Tenants.Services;

/// <summary>
/// Member management inside the current tenant. Changes take effect on the
/// member's next request: SessionValidator compares every access token with
/// the membership, and the refresh then issues the new role or ends the
/// session. Deactivating or removing a member also revokes their refresh
/// tokens for the tenant.
/// </summary>
public interface ITenantMemberService
{
    /// <summary>Requires tenant.members.read.</summary>
    Task<ServiceResult<List<TenantMemberDto>>> ListAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Requires tenant.members.manage.</summary>
    Task<ServiceResult<TenantMemberDto>> UpdateRoleAsync(
        int userId,
        string role,
        CancellationToken cancellationToken = default);

    /// <summary>Requires tenant.members.manage.</summary>
    Task<ServiceResult<TenantMemberDto>> UpdateStatusAsync(
        int userId,
        bool isActive,
        CancellationToken cancellationToken = default);

    /// <summary>Requires tenant.members.manage. Removes the membership, not the account.</summary>
    Task<ServiceResult<bool>> RemoveAsync(
        int userId,
        CancellationToken cancellationToken = default);
}

public sealed class TenantMemberService : ITenantMemberService
{
    private readonly IRepository<TenantMembership> _memberships;
    private readonly IRepository<User> _users;
    private readonly IRepository<AgentDelegationEntity> _delegations;
    private readonly IUserAuthorizationService _authorization;
    private readonly IExecutionContextAccessor _executionContext;
    private readonly IAuditService _auditService;
    private readonly IRepository<RefreshToken> _refreshTokens;
    private readonly SessionStateCache _sessions;

    public TenantMemberService(
        IRepository<TenantMembership> memberships,
        IRepository<User> users,
        IRepository<AgentDelegationEntity> delegations,
        IUserAuthorizationService authorization,
        IExecutionContextAccessor executionContext,
        IAuditService auditService,
        IRepository<RefreshToken> refreshTokens,
        SessionStateCache sessions)
    {
        _refreshTokens = refreshTokens;
        _sessions = sessions;
        _memberships = memberships;
        _users = users;
        _delegations = delegations;
        _authorization = authorization;
        _executionContext = executionContext;
        _auditService = auditService;
    }

    public async Task<ServiceResult<List<TenantMemberDto>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var (tenantId, currentUserId, error) = Authorize(Permissions.MembersRead);

        if (error != null)
            return ServiceResult<List<TenantMemberDto>>.Fail(error, 403);

        // TenantMembership is not tenant-filtered (users list their tenants
        // across all of them), so the tenant condition is explicit here.
        var memberships = await _memberships.ListAsync(
            x => x.TenantId == tenantId,
            cancellationToken);

        var userIds = memberships.Select(x => x.UserId).ToList();

        var users = await _users.ListAsync(
            x => userIds.Contains(x.Id),
            cancellationToken);

        var usersById = users.ToDictionary(x => x.Id);

        return ServiceResult<List<TenantMemberDto>>.Ok(
            memberships
                .Where(x => usersById.ContainsKey(x.UserId))
                .Select(x => Map(x, usersById[x.UserId], currentUserId))
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.Username, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    public async Task<ServiceResult<TenantMemberDto>> UpdateRoleAsync(
        int userId,
        string role,
        CancellationToken cancellationToken = default)
    {
        var canonicalRole = TenantRoles.Normalize(role);

        if (canonicalRole is null)
        {
            return ServiceResult<TenantMemberDto>.Fail(
                $"Role must be one of: {string.Join(", ", TenantRoles.All)}.",
                400);
        }

        var (membership, user, failure) =
            await LoadManageableMembershipAsync(userId, cancellationToken);

        if (failure != null)
            return Fail<TenantMemberDto>(failure.Value);

        if (membership!.Role == canonicalRole)
            return ServiceResult<TenantMemberDto>.Ok(Map(membership, user!, null));

        if (canonicalRole != TenantRoles.Admin &&
            await IsLastActiveAdminAsync(membership, cancellationToken))
        {
            return ServiceResult<TenantMemberDto>.Fail(
                "The tenant must keep at least one active Admin.",
                400);
        }

        var previousRole = membership.Role;

        membership.Role = canonicalRole;

        // Delegations were granted under the old role's permissions; the
        // member creates new ones under the new role.
        await RevokeDelegationsAsync(userId, cancellationToken);
        _sessions.Invalidate(userId);

        await _memberships.SaveChangesAsync(cancellationToken);

        await RecordAsync(
            "MemberRoleChanged",
            userId,
            $"Role changed from {previousRole} to {canonicalRole}.",
            cancellationToken);

        return ServiceResult<TenantMemberDto>.Ok(Map(membership, user!, null));
    }

    public async Task<ServiceResult<TenantMemberDto>> UpdateStatusAsync(
        int userId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var (membership, user, failure) =
            await LoadManageableMembershipAsync(userId, cancellationToken);

        if (failure != null)
            return Fail<TenantMemberDto>(failure.Value);

        if (membership!.IsActive == isActive)
            return ServiceResult<TenantMemberDto>.Ok(Map(membership, user!, null));

        if (!isActive &&
            await IsLastActiveAdminAsync(membership, cancellationToken))
        {
            return ServiceResult<TenantMemberDto>.Fail(
                "The tenant must keep at least one active Admin.",
                400);
        }

        membership.IsActive = isActive;

        if (!isActive)
        {
            await RevokeDelegationsAsync(userId, cancellationToken);
            await RevokeSessionsAsync(membership, cancellationToken);
        }

        await _memberships.SaveChangesAsync(cancellationToken);
        _sessions.Invalidate(userId);

        await RecordAsync(
            isActive ? "MemberActivated" : "MemberDeactivated",
            userId,
            null,
            cancellationToken);

        return ServiceResult<TenantMemberDto>.Ok(Map(membership, user!, null));
    }

    public async Task<ServiceResult<bool>> RemoveAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        var (membership, _, failure) =
            await LoadManageableMembershipAsync(userId, cancellationToken);

        if (failure != null)
            return Fail<bool>(failure.Value);

        if (await IsLastActiveAdminAsync(membership!, cancellationToken))
        {
            return ServiceResult<bool>.Fail(
                "The tenant must keep at least one active Admin.",
                400);
        }

        await RevokeDelegationsAsync(userId, cancellationToken);
        await RevokeSessionsAsync(membership!, cancellationToken);

        _memberships.Remove(membership!);

        await _memberships.SaveChangesAsync(cancellationToken);
        _sessions.Invalidate(userId);

        await RecordAsync(
            "MemberRemoved",
            userId,
            null,
            cancellationToken);

        return ServiceResult<bool>.Ok(true);
    }

    /// <summary>Ends the member's sessions in this tenant (saved with the caller's change).</summary>
    private async Task RevokeSessionsAsync(TenantMembership membership, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        foreach (var token in await _refreshTokens.ListTrackedAsync(
                     x => x.UserId == membership.UserId && x.TenantId == membership.TenantId && x.RevokedAtUtc == null,
                     cancellationToken))
        {
            token.RevokedAtUtc = now;
        }
    }

    private (int TenantId, int UserId, string? Error) Authorize(string permission)
    {
        var context = _executionContext.Current;

        if (context?.TenantId is null || context.Actor.UserId is null)
            return (0, 0, "A tenant must be selected.");

        if (!_authorization.HasPermission(permission))
            return (0, 0, "You are not authorized to manage members.");

        return (context.TenantId.Value, context.Actor.UserId.Value, null);
    }

    /// <summary>
    /// Loads the target's membership in the current tenant (tracked). Admins
    /// cannot change their own membership, so a tenant can never lock out the
    /// admin who is making the change.
    /// </summary>
    private async Task<(TenantMembership? Membership, User? User, (string Message, int Status)? Failure)>
        LoadManageableMembershipAsync(
            int userId,
            CancellationToken cancellationToken)
    {
        var (tenantId, currentUserId, error) = Authorize(Permissions.MembersManage);

        if (error != null)
            return (null, null, (error, 403));

        if (userId == currentUserId)
        {
            return (null, null, (
                "You cannot change your own membership. Ask another Admin.",
                400));
        }

        var membership = await _memberships.FirstOrDefaultTrackedAsync(
            x => x.TenantId == tenantId && x.UserId == userId,
            cancellationToken);

        var user = membership is null
            ? null
            : await _users.FirstOrDefaultAsync(
                x => x.Id == userId,
                cancellationToken);

        // Members of other tenants are reported as not found.
        if (membership is null || user is null)
            return (null, null, ("Member was not found.", 404));

        return (membership, user, null);
    }

    private async Task<bool> IsLastActiveAdminAsync(
        TenantMembership membership,
        CancellationToken cancellationToken)
    {
        if (!membership.IsActive || membership.Role != TenantRoles.Admin)
            return false;

        var activeAdmins = await _memberships.CountAsync(
            x =>
                x.TenantId == membership.TenantId &&
                x.IsActive &&
                x.Role == TenantRoles.Admin,
            cancellationToken);

        return activeAdmins <= 1;
    }

    /// <summary>
    /// Ends the member's MCP access in this tenant now (on deactivation,
    /// removal or role change), instead of when the delegations expire. Delegations are tenant-filtered, so only this
    /// tenant's are affected. Saved with the membership change.
    /// </summary>
    private async Task RevokeDelegationsAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        var delegations = await _delegations.ListTrackedAsync(
            x => x.UserId == userId && !x.Revoked,
            cancellationToken);

        foreach (var delegation in delegations)
        {
            delegation.Revoked = true;
        }
    }

    private Task RecordAsync(
        string action,
        int userId,
        string? reason,
        CancellationToken cancellationToken)
    {
        return _auditService.RecordAsync(
            AuditCategory.Tenant,
            action,
            AuditOutcome.Succeeded,
            actorType: AuditActorType.User,
            resourceType: "TenantMember",
            resourceId: userId.ToString(),
            reason: reason,
            cancellationToken: cancellationToken);
    }

    private static ServiceResult<T> Fail<T>((string Message, int Status) failure) =>
        ServiceResult<T>.Fail(failure.Message, failure.Status);

    private static TenantMemberDto Map(
        TenantMembership membership,
        User user,
        int? currentUserId)
    {
        return new TenantMemberDto
        {
            UserId = user.Id,
            Username = user.Username,
            Email = user.Email,
            Role = membership.Role,
            IsActive = membership.IsActive,
            JoinedAtUtc = membership.CreatedAtUtc,
            IsCurrentUser = user.Id == currentUserId
        };
    }
}
