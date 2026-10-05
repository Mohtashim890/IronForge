namespace IronForge.Application.Entities
{
    public class ConversationSummary
    {
        public long Id { get; set; }

        public Guid SessionId { get; set; }

        public int TenantId { get; set; }

        public long SummarizedThroughMessageId { get; set; }

        public string Content { get; set; } = "";

        public DateTime CreatedAtUtc { get; set; }

        public DateTime UpdatedAtUtc { get; set; }
    }
}
