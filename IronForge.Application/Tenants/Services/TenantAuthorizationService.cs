using IronForge.Application.Auth.Models;
using IronForge.Application.Execution;

namespace IronForge.Application.Tenants.Services
{
    public class TenantAuthorizationService
      : ITenantAuthorizationService
    {
        private readonly IExecutionContextAccessor _executionContext;

        public TenantAuthorizationService(
            IExecutionContextAccessor executionContext)
        {
            _executionContext = executionContext;
        }

        public ResourceAuthorizationResult Authorize(
            int resourceTenantId)
        {
            var context = _executionContext.Current;

            if (context == null)
            {
                return ResourceAuthorizationResult.Deny(
                    "Execution context is not established.");
            }

            if (!context.TenantId.HasValue)
            {
                return ResourceAuthorizationResult.Deny(
                    "Tenant context is not established.");
            }

            if (context.TenantId.Value != resourceTenantId)
            {
                return ResourceAuthorizationResult.Deny(
                    "Resource does not belong to the current tenant.");
            }

            return ResourceAuthorizationResult.Allow();
        }
    }
}
