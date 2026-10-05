namespace IronForge.Application.Agents.Delegations;

public interface IAgentDelegationCredentialService
{
    Task<string> CreateCredentialAsync(
        Guid delegationId,
        CancellationToken cancellationToken = default);
}