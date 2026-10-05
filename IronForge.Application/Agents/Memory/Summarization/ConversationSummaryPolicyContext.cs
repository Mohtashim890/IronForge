namespace IronForge.Application.Agents.Memory.Summarization
{
    public class ConversationSummaryPolicyContext
    {
        public int UnsummarizedMessageCount { get; init; }

        public int EstimatedUnsummarizedTokens { get; init; }

        public bool HasExistingSummary { get; init; }

        public bool IsSessionActive { get; init; }
    }
}
