using System.Collections.Concurrent;

namespace IronForge.Application.Agents.Delegations;

public class InMemoryAgentDelegationStore
    : IAgentDelegationStore
{
    private readonly ConcurrentDictionary<Guid, AgentDelegation>
        _delegations = new();

    public Task<AgentDelegation?> GetAsync(
        Guid delegationId,
        CancellationToken cancellationToken = default)
    {
        _delegations.TryGetValue(
            delegationId,
            out var delegation);

        return Task.FromResult(delegation);
    }

    public Task SaveAsync(
        AgentDelegation delegation,
        CancellationToken cancellationToken = default)
    {
        _delegations[delegation.DelegationId] = delegation;

        return Task.CompletedTask;
    }

    public Task DeleteAsync(
        Guid delegationId,
        CancellationToken cancellationToken = default)
    {
        _delegations.TryRemove(
            delegationId,
            out _);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AgentDelegation>> GetForUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    Task<Guid> IAgentDelegationStore.RevokeAsync(Guid delegationId, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}