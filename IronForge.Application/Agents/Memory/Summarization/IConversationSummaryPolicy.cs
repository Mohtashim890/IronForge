namespace IronForge.Application.Agents.Memory.Summarization
{
    public interface IConversationSummaryPolicy
    {
        bool ShouldSummarize(
        ConversationSummaryPolicyContext context);
    }
}
