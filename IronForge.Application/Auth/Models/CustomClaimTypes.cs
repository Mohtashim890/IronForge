namespace IronForge.Application.Auth.Models
{
    public class CustomClaimTypes
    {
        public const string TenantId = "tenant_id";
        public const string Permission = "permission";
        public const string AgentId = "agent_id";

        /// <summary>The account's security stamp when the token was issued.</summary>
        public const string SecurityStamp = "sstamp";
    }
}
