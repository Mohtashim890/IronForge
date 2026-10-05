using IronForge.Shared.Authorization;

namespace IronForge.Application.Auth.Models
{
    public static class RolePermissions
    {
        public static string[] GetPermissions(
        string role)
        {
            return role switch
            {
                "Admin" =>
                [
                    Permissions.ProductsRead,
                    Permissions.ProductsCreate,
                    Permissions.ProductsUpdate,
                    Permissions.ProductsDelete,

                    Permissions.AgentsCreate,
                    Permissions.AgentsRead,
                    Permissions.AgentsUpdate,
                    Permissions.AgentsDelete,
                    Permissions.AgentsPermissionsManage,

                    Permissions.McpClientsRead,
                    Permissions.McpClientsCreate,
                    Permissions.McpClientsUpdate,
                    Permissions.McpClientsDelete,
                    Permissions.AgentsMcpClientsManage,

                    Permissions.AuditRead
                ],

                "User" =>
                [
                    Permissions.ProductsRead,
                    Permissions.ProductsCreate,
                    Permissions.ProductsUpdate,

                    Permissions.AuditRead
                ],

                _ =>
                []
            };
        }
    }
}
