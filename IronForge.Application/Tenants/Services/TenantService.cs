using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.Persistence;
using IronForge.Application.Tenants.Models;

namespace IronForge.Application.Tenants.Services
{
    public class TenantService : ITenantService
    {
        private readonly IRepository<TenantMembership> _memberships;
        private readonly IRepository<Tenant> _tenants;
        private readonly IExecutionContextAccessor _executionContext;
        private readonly IAuditService _auditService;

        public TenantService(
            IRepository<TenantMembership> memberships,
            IRepository<Tenant> tenants,
            IExecutionContextAccessor executionContext,
            IAuditService auditService)
        {
            _memberships = memberships;
            _tenants = tenants;
            _executionContext = executionContext;
            _auditService = auditService;
        }

        public async Task<IReadOnlyList<TenantMembershipResult>>
    GetMyTenantsAsync(
        CancellationToken cancellationToken = default)
        {
            var context = _executionContext.Current;

            if (context == null)
                throw new InvalidOperationException(
                    "Execution context is not established.");

            var userId = context.Actor.UserId;

            if (!userId.HasValue)
                throw new InvalidOperationException(
                    "User identity is not established.");

            var memberships = await _memberships.ListAsync(
                x => x.UserId == userId.Value && x.IsActive,
                cancellationToken);

            var tenantIds = memberships
                .Select(x => x.TenantId)
                .Distinct()
                .ToList();

            var tenants = await _tenants.ListAsync(
                x => tenantIds.Contains(x.Id) && x.IsActive,
                cancellationToken);

            var tenantById = tenants.ToDictionary(x => x.Id);

            var results = memberships
                .Where(x => tenantById.ContainsKey(x.TenantId))
                .Select(x =>
                {
                    var tenant = tenantById[x.TenantId];

                    return new TenantMembershipResult
                    {
                        TenantId = x.TenantId,
                        TenantName = tenant.Name,
                        Slug = tenant.Slug,
                        Role = x.Role,
                        IsActive = tenant.IsActive
                    };
                })
                .ToList();

            return results;
        }

        public async Task<TenantSelectionResult> SelectAsync(
            int tenantId,
            CancellationToken cancellationToken = default)
        {
            var context = _executionContext.Current;

            if (context == null)
            {
                return TenantSelectionResult.Failure(
                    "Execution context is not established.");
            }

            var userId = context.Actor.UserId;

            if (!userId.HasValue)
            {
                return TenantSelectionResult.Failure(
                    "User identity is not established.");
            }

            var tenant = await _tenants.FirstOrDefaultAsync(
                x => x.Id == tenantId,
                cancellationToken);

            if (tenant == null)
            {
                await _auditService.RecordAsync(
                    AuditCategory.Tenant,
                    "TenantSelectionDenied",
                    AuditOutcome.Denied,
                    actorType: AuditActorType.User,
                    resourceType: "Tenant",
                    resourceId: tenantId.ToString(),
                    reason: "Tenant was not found.");

                return TenantSelectionResult.Failure(
                    "Tenant was not found.");
            }

            if (!tenant.IsActive)
            {
                await _auditService.RecordAsync(
                   AuditCategory.Tenant,
                   "TenantSelectionDenied",
                   AuditOutcome.Denied,
                   actorType: AuditActorType.User,
                   resourceType: "Tenant",
                   resourceId: tenantId.ToString(),
                   reason: "Tenant is inactive.");

                return TenantSelectionResult.Failure(
                    "Tenant is inactive.");
            }

            var membership = await _memberships.FirstOrDefaultAsync(
                x =>
                    x.TenantId == tenantId &&
                    x.UserId == userId.Value,
                cancellationToken);

            if (membership == null)
            {
                await _auditService.RecordAsync(
                   AuditCategory.Tenant,
                   "TenantSelectionDenied",
                   AuditOutcome.Denied,
                   actorType: AuditActorType.User,
                   resourceType: "Tenant",
                   resourceId: tenantId.ToString(),
                   reason: "User is not a member of the tenant.");

                return TenantSelectionResult.Failure(
                    "User is not a member of this tenant.");
            }

            if (!membership.IsActive)
            {
                await _auditService.RecordAsync(
                    AuditCategory.Tenant,
                    "TenantSelectionDenied",
                    AuditOutcome.Denied,
                    actorType: AuditActorType.User,
                    resourceType: "Tenant",
                    resourceId: tenantId.ToString(),
                    reason: "Tenant membership is inactive.");

                return TenantSelectionResult.Failure(
                    "Tenant membership is inactive.");
            }

            return TenantSelectionResult.Success(
                tenant.Id,
                tenant.Name,
                membership.Role);
        }
    }
}
