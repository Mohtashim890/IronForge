namespace IronForge.Mcp.Authorization;

public interface IMcpAuthorizationService
{
    Task<(bool Allowed, string Reason)> AuthorizeConnectionAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorizes one tool call: user, agent and delegation permissions, then
    /// the same governance as internal agents (binding, subscription module,
    /// approval, amount limits) evaluated on the call's arguments.
    /// </summary>
    Task<(bool Allowed, string Reason)> AuthorizeToolAsync(
        string toolName,
        IDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default);
}
