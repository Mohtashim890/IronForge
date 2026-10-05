using IronForge.Application.Entities;

namespace IronForge.Application.Agents.Memory.Summarization
{
    public interface IAgentConversationSummarizer
    {
        Task<string> SummarizeAsync(
        IReadOnlyList<ConversationMessage> messages,
        string? existingSummary = null,
        CancellationToken cancellationToken = default);
    }
}
