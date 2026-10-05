namespace IronForge.Application.Agents.Delegations;

public interface IAgentDelegationStore
{
    Task<AgentDelegation?> GetAsync(
        Guid delegationId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        AgentDelegation delegation,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid delegationId,
        CancellationToken cancellationToken = default);

    Task<Guid> RevokeAsync(
       Guid delegationId,
       CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentDelegation>> GetForUserAsync(
        int userId,
        CancellationToken cancellationToken = default);
}