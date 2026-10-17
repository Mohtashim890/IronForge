using IronForge.Application.Entities;
using IronForge.Application.Gym.Members;
using IronForge.Application.Gym.Notifications;
using IronForge.Infrastructure.Data;
using IronForge.Shared.Authorization;
using IronForge.Shared.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace IronForge.Infrastructure.Persistence;

/// <summary>
/// See <see cref="IGymDirectory"/>. The tenant query filter is bypassed on
/// purpose and replaced by an explicit tenant condition in every query.
/// </summary>
public sealed class GymDirectory : IGymDirectory
{
    private readonly IronForgeDbContext _dbContext;

    public GymDirectory(IronForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<List<MembershipPlan>> ListActivePlansAsync(
        int tenantId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.MembershipPlans
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive)
            .OrderBy(x => x.Price)
            .ToListAsync(cancellationToken);
    }

    public Task<MemberProfile?> FindMemberByEmailAsync(
        int tenantId,
        string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.MemberProfiles
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.TenantId == tenantId && x.Email == normalizedEmail,
                cancellationToken);
    }

    public Task<MemberProfile?> FindUnlinkedMemberByEmailTrackedAsync(
        int tenantId,
        string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.MemberProfiles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                x => x.TenantId == tenantId &&
                     x.Email == normalizedEmail &&
                     x.UserId == null &&
                     x.AnonymizedAtUtc == null,
                cancellationToken);
    }

    public async Task<List<int>> ListServedTenantIdsAsync(
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var tenants = await _dbContext.Tenants
            .AsNoTracking()
            .Where(t => t.IsActive && _dbContext.GymSettings.Any(g => g.TenantId == t.Id))
            .ToListAsync(cancellationToken);

        return tenants
            .Where(t => t.HasAccess(nowUtc))
            .Select(t => t.Id)
            .ToList();
    }

    public async Task<List<NotificationRecipient>> ListAdminRecipientsAsync(
        int tenantId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.TenantMemberships
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.IsActive && m.Role == TenantRoles.Admin)
            .Join(
                _dbContext.Users.Where(u => u.IsActive),
                m => m.UserId,
                u => u.Id,
                (m, u) => new NotificationRecipient(u.Id, u.Email, u.Username))
            .ToListAsync(cancellationToken);
    }
}
