namespace IronForge.Application.Entities
{
    public class Tenant
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public string Slug { get; set; } = "";

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAtUtc { get; set; }

        /// <summary>
        /// Monthly agent token limit for this tenant. Null uses the platform
        /// default (AgentQuotas:DefaultMonthlyTokenLimit). Set by the platform
        /// operator, never by the tenant.
        /// </summary>
        public long? MonthlyAgentTokenLimit { get; set; }
    }
}
