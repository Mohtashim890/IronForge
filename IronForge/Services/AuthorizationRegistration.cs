using IronForge.Shared.Authorization;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Services
{
    public static class AuthorizationRegistration
    {
        public static IServiceCollection AddIronForgeMAUIAuthorization(
            this IServiceCollection services)
        {
            services.AddSingleton<
                IAuthorizationPolicyProvider,
                PermissionPolicyProvider>();

            services.AddAuthorizationCore();

            return services;
        }
    }
}
