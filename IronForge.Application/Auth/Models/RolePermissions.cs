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
                    Permissions.GymMembersRead,
                    Permissions.GymMembersManage,
                    Permissions.GymPlansManage,
                    Permissions.GymBillingRead,
                    Permissions.GymBillingManage,
                    Permissions.GymCheckInsManage,
                    Permissions.GymRoutinesManage,
                    Permissions.GymNotificationsSend,
                    Permissions.GymSettingsManage,

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

                // Front-desk and coaching staff: no billing, settings or staff management.
                TenantRoles.Trainer =>
                [
                    Permissions.GymMembersRead,
                    Permissions.GymCheckInsManage,
                    Permissions.GymRoutinesManage,

                    Permissions.AgentsRead
                ],

                // A gym member: self-service on their own profile only.
                TenantRoles.Member =>
                [
                    Permissions.GymSelfRead,
                    Permissions.GymSelfWorkoutsWrite,
                    Permissions.GymSelfPayments
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
