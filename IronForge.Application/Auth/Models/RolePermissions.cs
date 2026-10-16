using IronForge.Shared.Authorization;

namespace IronForge.Application.Auth.Models
{
    public static class RolePermissions
    {
        /// <summary>Permissions of a tenant role (TenantMembership.Role).</summary>
        public static string[] GetPermissions(
        string role)
        {
            return role switch
            {
                TenantRoles.Admin =>
                [
                    Permissions.ProductsRead,
                    Permissions.ProductsCreate,
                    Permissions.ProductsUpdate,
                    Permissions.ProductsDelete,
                    Permissions.ProductsManageAll,

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
                    Permissions.ApprovalsDecide,

                    Permissions.AuditRead,

                    Permissions.MembersRead,
                    Permissions.MembersManage
                ],

                // "User" is the legacy name of the Member role.
                TenantRoles.Member or "User" =>
                [
                    Permissions.ProductsRead,
                    Permissions.ProductsCreate,
                    Permissions.ProductsUpdate,

                    Permissions.AuditRead
                ],

                TenantRoles.Viewer =>
                [
                    Permissions.ProductsRead,

                    Permissions.AuditRead
                ],

                _ =>
                []
            };
        }

        /// <summary>
        /// Permissions of an account-wide role (User.Role). They are added to
        /// every access token, with or without a selected tenant.
        /// </summary>
        public static string[] GetPlatformPermissions(
            string platformRole)
        {
            return platformRole switch
            {
                PlatformRoles.PlatformAdmin =>
                [
                    Permissions.PlatformTenantsManage
                ],

                _ =>
                []
            };
        }
    }
}
