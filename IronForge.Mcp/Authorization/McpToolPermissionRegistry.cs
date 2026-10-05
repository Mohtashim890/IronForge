using IronForge.Shared.Authorization;

namespace IronForge.Mcp.Authorization;

public sealed class McpToolPermissionRegistry
    : IMcpToolPermissionRegistry
{
    private static readonly IReadOnlyDictionary<string, string> ToolPermissions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["get_products"] = Permissions.ProductsRead,
            ["get_product"] = Permissions.ProductsRead,
            ["create_product"] = Permissions.ProductsCreate,
            ["update_product"] = Permissions.ProductsUpdate,
            ["delete_product"] = Permissions.ProductsDelete
        };

    public bool TryGetRequiredPermission(
        string toolName,
        out string requiredPermission)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            requiredPermission = string.Empty;
            return false;
        }

        return ToolPermissions.TryGetValue(
            toolName,
            out requiredPermission!);
    }
}
