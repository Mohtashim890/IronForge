using IronForge.Application.Commons;
using IronForge.Shared.Models.Tenants;

namespace IronForge.Application.Tenants.Services;

/// <summary>
/// Read-only, cross-tenant views for the platform operator. Every method
/// requires platform.tenants.manage; the implementation reads across tenant
/// query filters on purpose.
/// </summary>
public interface IPlatformInsightsService
{
    Task<ServiceResult<PlatformOverviewDto>> GetOverviewAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult<PlatformTenantDetailDto>> GetTenantAsync(int tenantId, CancellationToken cancellationToken = default);

    Task<ServiceResult<PlatformCatalogDto>> GetCatalogAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult<List<PlatformAuditEventDto>>> GetAuditAsync(
        PlatformAuditQuery query,
        CancellationToken cancellationToken = default);
}
