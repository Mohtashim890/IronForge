using IronForge.Shared.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace IronForge.Platform.Services
{
    public static class AuthorizationRegistration
    {
        public static IServiceCollection AddIronForgeWebAuthorization(
            this IServiceCollection services)
        {
            services.AddSingleton<
                IAuthorizationPolicyProvider,
                PermissionPolicyProvider>();

            services.AddAuthorization();

            return services;
        }
    }
}
