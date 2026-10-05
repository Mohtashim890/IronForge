namespace IronForge.Mcp.Authorization;

public interface IMcpAuthorizationService
{
    Task<(bool Allowed, string Reason)> AuthorizeConnectionAsync(
        CancellationToken cancellationToken = default);

    Task<(bool Allowed, string Reason)> AuthorizeToolAsync(
        string toolName,
        CancellationToken cancellationToken = default);
}
