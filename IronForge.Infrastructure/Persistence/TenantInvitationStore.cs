using IronForge.Application.Entities;
using IronForge.Application.Persistence;
using IronForge.Application.Tenants.Invitations;
using IronForge.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace IronForge.Infrastructure.Persistence;

/// <summary>
/// See <see cref="ITenantInvitationStore"/>. The tenant query filter is
/// bypassed on purpose and replaced by an explicit tenant condition (or the
/// token, which is unique and bound to one tenant).
/// </summary>
public sealed class TenantInvitationStore : ITenantInvitationStore
{
    private readonly IronForgeDbContext _dbContext;

    public TenantInvitationStore(IronForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<TenantInvitation?> FindByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.TenantInvitations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                x => x.TokenHash == tokenHash,
                cancellationToken);
    }

    public Task<TenantInvitation?> FindAsync(
        int tenantId,
        Guid invitationId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.TenantInvitations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                x => x.TenantId == tenantId && x.Id == invitationId,
                cancellationToken);
    }

    public Task<List<TenantInvitation>> ListOpenAsync(
        int tenantId,
        string? email = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.TenantInvitations
            .IgnoreQueryFilters()
            .Where(x =>
                x.TenantId == tenantId &&
                x.AcceptedAtUtc == null &&
                x.RevokedAtUtc == null);

        if (email != null)
        {
            query = query.Where(x => x.Email == email);
        }

        return query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public void Add(TenantInvitation invitation)
    {
        _dbContext.TenantInvitations.Add(invitation);
    }

    public async Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(ex);
        }
    }
}
