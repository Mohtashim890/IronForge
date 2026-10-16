using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Shared.Authorization
{
    public static class Permissions
    {
        public const string ProductsRead = "products.read";
        public const string ProductsCreate = "products.create";
        public const string ProductsUpdate = "products.update";
        public const string ProductsDelete = "products.delete";

        /// <summary>Act on every product, not only your own (ownership bypass).</summary>
        public const string ProductsManageAll = "products.manage-all";

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

        // Platform-level: granted by User.Role, independent of the selected tenant.
        public const string PlatformTenantsManage = "platform.tenants.manage";
    }
}
