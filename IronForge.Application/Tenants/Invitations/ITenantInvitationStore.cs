using IronForge.Application.Entities;

namespace IronForge.Application.Tenants.Invitations;

/// <summary>
/// Invitation persistence. Invitations are used in flows that run without a
/// tenant context (platform provisioning, anonymous acceptance), where the
/// tenant query filter cannot apply. Every method therefore takes the tenant
/// explicitly or, for acceptance, resolves it from the token.
/// </summary>
public interface ITenantInvitationStore
{
    /// <summary>Tracked. Searches all tenants: the token identifies the tenant.</summary>
    Task<TenantInvitation?> FindByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default);

    /// <summary>Tracked.</summary>
    Task<TenantInvitation?> FindAsync(
        int tenantId,
        Guid invitationId,
        CancellationToken cancellationToken = default);

    /// <summary>Not accepted and not revoked (includes expired ones). Tracked.</summary>
    Task<List<TenantInvitation>> ListOpenAsync(
        int tenantId,
        string? email = null,
        CancellationToken cancellationToken = default);

    void Add(TenantInvitation invitation);

    /// <summary>
    /// Saves all pending changes of the request's DbContext.
    /// Throws <see cref="Persistence.ConcurrencyConflictException"/> when an
    /// invitation was changed concurrently (e.g. accepted twice).
    /// </summary>
    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);
}
