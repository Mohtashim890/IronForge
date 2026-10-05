using IronForge.Application.Agents.Models;
using IronForge.Application.Auth.Models;
using IronForge.Application.Execution;
using IronForge.Mcp.Authorization;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace IronForge.Mcp.Identity;

public sealed class McpIdentityMiddleware
{
    private readonly RequestDelegate _next;

    public McpIdentityMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IExecutionContextAccessor executionContextAccessor,
        IMcpAuthorizationService mcpAuthorizationService)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        var userIdClaim =
            context.User.FindFirst(JwtRegisteredClaimNames.Sub)
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier);

        var tenantIdValue =
            context.User.FindFirstValue(CustomClaimTypes.TenantId);

        if (!int.TryParse(tenantIdValue, out var tenantId))
        {
            await ForbidAsync(
                context,
                "Tenant identity is missing or invalid.");

            return;
        }

        if (!int.TryParse(userIdClaim?.Value, out var userId))
        {
            await ForbidAsync(
                context,
                "User identity is invalid.");

            return;
        }

        var agentId =
            context.User.FindFirstValue(CustomClaimTypes.AgentId);

        if (string.IsNullOrWhiteSpace(agentId))
        {
            await ForbidAsync(
                context,
                "Agent identity is missing.");

            return;
        }

        var clientId =
            context.User.FindFirstValue(CustomClaimTypes.ClientId);

        if (string.IsNullOrWhiteSpace(clientId))
        {
            await ForbidAsync(
                context,
                "MCP client identity is missing.");

            return;
        }

        var delegationIdValue =
            context.User.FindFirstValue(CustomClaimTypes.DelegationId);

        if (!Guid.TryParse(
                delegationIdValue,
                out var delegationId))
        {
            await ForbidAsync(
                context,
                "Delegation identity is missing or invalid.");

            return;
        }

        var username =
            context.User.FindFirstValue(ClaimTypes.Name);

        var role =
            context.User.FindFirstValue(ClaimTypes.Role);

        var activity = Activity.Current;

        var userPermissions =
            context.User
                .FindAll(CustomClaimTypes.Permission)
                .Select(x => x.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var actor = new ExecutionActor
        {
            UserId = userId,
            Username = username,
            Role = role,
            UserPermissions = userPermissions,
            Agent = new AgentIdentity
            {
                AgentId = agentId,
                Name = agentId,
                Version = "1.0",
                Type = AgentType.External
            },
            ClientId = clientId,
            DelegationId = delegationId
        };

        var executionContext =
            new Application.Execution.ExecutionContext
            {
                TenantId = tenantId,
                Actor = actor,
                Source = ExecutionSource.Mcp,
                CorrelationId = context.TraceIdentifier,
                TraceId = activity?.TraceId.ToString(),
                SpanId = activity?.SpanId.ToString()
            };

        executionContextAccessor.Set(executionContext);

        var authorization =
            await mcpAuthorizationService.AuthorizeConnectionAsync(
                context.RequestAborted);

        if (!authorization.Allowed)
        {
            await ForbidAsync(
                context,
                authorization.Reason);

            return;
        }

        await _next(context);
    }

    private static async Task ForbidAsync(
        HttpContext context,
        string message)
    {
        context.Response.StatusCode =
            StatusCodes.Status403Forbidden;

        await context.Response.WriteAsJsonAsync(
            new
            {
                Error = message
            });
    }
}