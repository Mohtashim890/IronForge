using System.Text.Json;
using IronForge.Application.Agents.Governance;
using IronForge.Mcp.Authorization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace IronForge.Mcp.Tools;

/// <summary>
/// MCP tools are the agent tool catalogue: there are no MCP-specific tool
/// classes. tools/list returns what the calling agent may use; tools/call
/// authorizes and runs the catalogue tool through its governed function.
/// Both run in the request scope, after McpIdentityMiddleware has
/// established the external agent's execution context.
/// </summary>
public static class McpCatalogueHandlers
{
    public static async ValueTask<ListToolsResult> ListToolsAsync(
        RequestContext<ListToolsRequestParams> request,
        CancellationToken cancellationToken)
    {
        var tools = Services(request).GetRequiredService<IExternalAgentToolService>();

        var functions = await tools.GetCallableToolsAsync(cancellationToken);

        return new ListToolsResult
        {
            Tools = functions
                .Select(function => new Tool
                {
                    Name = function.Name,
                    Description = function.Description,
                    InputSchema = function.JsonSchema
                })
                .ToList()
        };
    }

    public static async ValueTask<CallToolResult> CallToolAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken)
    {
        var services = Services(request);
        var toolName = request.Params?.Name ?? "";

        var arguments =
            request.Params?.Arguments?.ToDictionary(
                x => x.Key,
                x => (object?)x.Value,
                StringComparer.Ordinal)
            ?? new Dictionary<string, object?>(StringComparer.Ordinal);

        // User ∩ agent ∩ delegation, then the in-app governance rules, with
        // a specific reason when refused.
        var authorization =
            await services
                .GetRequiredService<IMcpAuthorizationService>()
                .AuthorizeToolAsync(toolName, arguments, cancellationToken);

        if (!authorization.Allowed)
            return Error(authorization.Reason);

        var (success, result, error) =
            await services
                .GetRequiredService<IExternalAgentToolService>()
                .InvokeAsync(toolName, arguments, cancellationToken);

        if (!success)
            return Error(error);

        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock
                {
                    Text = JsonSerializer.Serialize(result)
                }
            ]
        };
    }

    private static IServiceProvider Services<TParams>(RequestContext<TParams> request) =>
        request.Services
        ?? throw new InvalidOperationException("MCP request services are unavailable.");

    private static CallToolResult Error(string message) =>
        new()
        {
            IsError = true,
            Content = [new TextContentBlock { Text = message }]
        };
}
