using IronForge.Application.Tenants.Models;

namespace IronForge.Application.Tenants.Services
{
    public interface ITenantService
    {
        Task<IReadOnlyList<TenantMembershipResult>> GetMyTenantsAsync(
        CancellationToken cancellationToken = default);
        Task<TenantSelectionResult> SelectAsync(
            int tenantId,
            CancellationToken cancellationToken = default);
    }
}
