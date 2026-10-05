namespace IronForge.Application.Agents.State
{
    public interface IAgentStateStore
    {
        Task<AgentConversation?> GetAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

        Task SaveAsync(
            AgentConversation conversation,
            CancellationToken cancellationToken = default);

        Task DeleteAsync(
            string sessionId,
            CancellationToken cancellationToken = default);
    }
}
