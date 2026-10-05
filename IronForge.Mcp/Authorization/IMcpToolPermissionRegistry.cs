namespace IronForge.Mcp.Authorization;

public interface IMcpToolPermissionRegistry
{
    bool TryGetRequiredPermission(
        string toolName,
        out string requiredPermission);
}
