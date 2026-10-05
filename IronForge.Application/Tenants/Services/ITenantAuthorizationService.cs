using IronForge.Application.Auth.Models;

namespace IronForge.Application.Tenants.Services
{
    public interface ITenantAuthorizationService
    {
        ResourceAuthorizationResult Authorize(
            int resourceTenantId);
    }
}
