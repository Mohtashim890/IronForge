using IronForge.Application.Entities;

namespace IronForge.Application.Agents.Memory.ConversationSession
{
    public interface IConversationService
    {
        Task<AgentSession> CreateSessionAsync(
            string agentId,
            string? title = null,
            CancellationToken cancellationToken = default);

        Task<AgentSession?> GetSessionAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<AgentSession>> GetMySessionsAsync(
            string? agentId = null,
            CancellationToken cancellationToken = default);

        Task<ConversationMessage> AddMessageAsync(
            Guid sessionId,
            ConversationMessageRole role,
            string content,
            string? toolCallId = null,
            string? toolName = null,
            string? metadataJson = null,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<ConversationMessage>?> GetMessagesAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default);

        Task ArchiveSessionAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default);
    }
}
