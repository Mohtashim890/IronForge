using IronForge.Application.Auth.Models;
using IronForge.Application.Execution;
using Microsoft.AspNetCore.Http;
using System.Diagnostics;
using System.Security.Claims;

public class ExecutionContextMiddleware
{
    private readonly RequestDelegate _next;

    public ExecutionContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext httpContext,
        IExecutionContextAccessor accessor)
    {
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            var userIdValue = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier); 
            int? userId = null;
            if (int.TryParse(userIdValue, out var parsedUserId))
            {
                userId = parsedUserId;
            }

            var tenantIdValue = httpContext.User.FindFirstValue(CustomClaimTypes.TenantId);
            int? tenantId = null;
            if (int.TryParse(
                tenantIdValue,
                out var parsedTenantId))
            {
                tenantId = parsedTenantId;
            }

            var username = httpContext.User.FindFirstValue(ClaimTypes.Name);

            var role = httpContext.User.FindFirstValue(ClaimTypes.Role);

            var agentId =
                httpContext.User.FindFirstValue(CustomClaimTypes.AgentId);

            var clientId =
                httpContext.User.FindFirstValue(CustomClaimTypes.ClientId);

            var permissions =
                httpContext.User
                    .FindAll(CustomClaimTypes.Permission)
                    .Select(c => c.Value)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var activity = Activity.Current;

            var context = new IronForge.Application.Execution.ExecutionContext
            {
                TenantId = tenantId,
                Source = ExecutionSource.Web,
                Actor = new IronForge.Application.Execution.ExecutionActor
                {
                    UserId = userId,
                    Username = username,
                    Agent = null,
                    ClientId = clientId,
                    UserPermissions = permissions,
                    Role = role
                },
                CorrelationId = httpContext.TraceIdentifier,
                TraceId = activity?.TraceId.ToString(),
                SpanId = activity?.SpanId.ToString()
            };

            accessor.Set(context);
        }

        await _next(httpContext);
    }
}