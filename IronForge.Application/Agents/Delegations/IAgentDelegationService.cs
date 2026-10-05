namespace IronForge.Application.Agents.Delegations;

public interface IAgentDelegationService
{
    Task EstablishAsync(
    Guid delegationId,
    CancellationToken cancellationToken = default);
    Task<AgentDelegation> ValidateAsync(
    Guid delegationId,
    CancellationToken cancellationToken = default);

    Task<AgentDelegation> CreateAsync(
    string agentId,
    string clientId,
    IEnumerable<string> scopes,
    TimeSpan lifetime,
    CancellationToken cancellationToken = default);

    Task<AgentDelegation?> GetAsync(
        Guid delegationId,
        CancellationToken cancellationToken = default);

    Task RevokeAsync(
        Guid delegationId,
        CancellationToken cancellationToken = default);
    Task<AgentDelegation> GetForCurrentUserAsync(
    Guid delegationId,
    CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentDelegation>> GetForCurrentUserAsync(
            CancellationToken cancellationToken = default);
}