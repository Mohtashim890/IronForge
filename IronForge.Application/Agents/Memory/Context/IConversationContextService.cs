using Microsoft.Extensions.AI;

namespace IronForge.Application.Agents.Memory.Context
{
    public interface IConversationContextService
    {
        Task<IReadOnlyList<ChatMessage>> BuildContextAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default);
    }
}
