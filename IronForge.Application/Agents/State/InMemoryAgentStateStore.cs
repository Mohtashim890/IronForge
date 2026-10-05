using System.Collections.Concurrent;

namespace IronForge.Application.Agents.State;

public class InMemoryAgentStateStore : IAgentStateStore
{
    private readonly ConcurrentDictionary<
        string,
        AgentConversation> _conversations = new();

    public Task<AgentConversation?> GetAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        _conversations.TryGetValue(
            sessionId,
            out var conversation);

        return Task.FromResult(conversation);
    }

    public Task SaveAsync(
        AgentConversation conversation,
        CancellationToken cancellationToken = default)
    {
        _conversations[
            conversation.SessionId] = conversation;

        return Task.CompletedTask;
    }

    public Task DeleteAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        _conversations.TryRemove(
            sessionId,
            out _);

        return Task.CompletedTask;
    }
}