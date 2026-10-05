namespace IronForge.Application.Agents.Memory.Summarization
{
    public interface IConversationSummaryCoordinator
    {
        Task EvaluateAndSummarizeAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);
    }
}
