using IronForge.Application.Entities;

namespace IronForge.Application.Agents.Memory.Summarization
{
    public interface IConversationSummaryService
    {
        Task<ConversationSummary?> GetAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

        Task<ConversationSummary> CreateOrUpdateAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default);
    }
}
