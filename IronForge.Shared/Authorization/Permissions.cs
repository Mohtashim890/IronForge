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

        public const string MemoryRead = "memory.read";
        public const string MemoryCreate = "memory.create";
        public const string MemoryDelete = "memory.delete";

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
    }
}
