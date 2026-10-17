using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace IronForge.Api.Helpers
{
    /// <summary>
    /// Per-client-IP limits for anonymous endpoints (sign-in, joining a gym,
    /// password reset, payment callbacks), against credential stuffing and abuse.
    /// </summary>
    public static class RateLimitPolicies
    {
        public const string Auth = "auth";
        public const string Anonymous = "anonymous";
        public const string Webhook = "webhook";

        public static IServiceCollection AddIronForgeRateLimiting(this IServiceCollection services)
        {
            return services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                options.AddPolicy(Auth, context => PerIp(context, permitLimit: 10, window: TimeSpan.FromMinutes(1)));
                options.AddPolicy(Anonymous, context => PerIp(context, permitLimit: 30, window: TimeSpan.FromMinutes(1)));
                options.AddPolicy(Webhook, context => PerIp(context, permitLimit: 300, window: TimeSpan.FromMinutes(1)));
            });
        }

        private static RateLimitPartition<string> PerIp(HttpContext context, int permitLimit, TimeSpan window) =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = window,
                    QueueLimit = 0
                });
    }
}
