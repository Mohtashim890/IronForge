using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Shared.Authorization
{
    public static class Permissions
    {
        public const string AuditRead = "audit.read";

        public const string AgentsRead = "agents.read";
        public const string AgentsCreate = "agents.create";
        public const string AgentsUpdate = "agents.update";
        public const string AgentsDelete = "agents.delete";
        public const string AgentsPermissionsManage = "agents.permissions.manage";

        public const string McpClientsRead = "mcp.clients.read";
        public const string McpClientsCreate = "mcp.clients.create";
        public const string McpClientsUpdate = "mcp.clients.update";
        public const string McpClientsDelete = "mcp.clients.delete";
        public const string AgentsMcpClientsManage = "agents.mcp-clients.manage";

        /// <summary>Decide High/Critical-risk agent actions requested by others (four-eyes).</summary>
        public const string ApprovalsDecide = "agents.approvals.decide";

        public const string MembersRead = "tenant.members.read";
        public const string MembersManage = "tenant.members.manage";

        // Gym operations (staff).
        public const string GymMembersRead = "gym.members.read";
        public const string GymMembersManage = "gym.members.manage";
        public const string GymPlansManage = "gym.plans.manage";
        public const string GymBillingRead = "gym.billing.read";
        public const string GymBillingManage = "gym.billing.manage";
        public const string GymCheckInsManage = "gym.checkins.manage";
        public const string GymRoutinesManage = "gym.routines.manage";
        public const string GymNotificationsSend = "gym.notifications.send";
        public const string GymSettingsManage = "gym.settings.manage";

        /// <summary>
        /// Held only by a paired check-in device (kiosk), never by a role: it
        /// can scan member passes and nothing else.
        /// </summary>
        public const string GymCheckInsDevice = "gym.checkins.device";

        /// <summary>
        /// Held only by a registered access terminal (ZKTeco), never by a
        /// role: it uploads entries and enrolled users and receives its
        /// user list. Nothing else.
        /// </summary>
        public const string GymAccessTerminal = "gym.access.terminal";

        // Gym member self-service: always limited to the caller's own profile.
        public const string GymSelfRead = "gym.self.read";
        public const string GymSelfWorkoutsWrite = "gym.self.workouts.write";
        public const string GymSelfPayments = "gym.self.payments";

        // Platform-level: granted by User.Role, independent of the selected tenant.
        public const string PlatformTenantsManage = "platform.tenants.manage";
    }
}
