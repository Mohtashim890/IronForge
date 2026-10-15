namespace IronForge.Application.Entities
{
    /// <summary>
    /// Token usage of one agent model call (including all function-calling
    /// round trips inside it). The basis for tenant quotas and cost reporting.
    /// </summary>
    public class AgentUsageRecord
    {
        public long Id { get; set; }

        public int TenantId { get; set; }

        public int UserId { get; set; }

        public string AgentId { get; set; } = "";

        public Guid? SessionId { get; set; }

        /// <summary>"run" or "approval".</summary>
        public string Operation { get; set; } = "";

        public string ModelProfile { get; set; } = "";

        public string Model { get; set; } = "";

        public long InputTokens { get; set; }

        public long OutputTokens { get; set; }

        public long TotalTokens { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }
}
