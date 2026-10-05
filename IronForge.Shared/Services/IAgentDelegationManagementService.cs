using IronForge.Shared.Models;
using IronForge.Shared.Models.Delegations;

namespace IronForge.Shared.Services;

public interface IAgentDelegationManagementService
{
    Task<ServiceResult<List<AgentDelegationDto>>>
        GetMyDelegationsAsync(
            CancellationToken cancellationToken = default);

    Task<ServiceResult<AgentDelegationDto>>
        GetDelegationAsync(
            Guid delegationId,
            CancellationToken cancellationToken = default);

    Task<ServiceResult<AgentDelegationDto>>
        CreateDelegationAsync(
            CreateAgentDelegationRequest request,
            CancellationToken cancellationToken = default);

    Task<ServiceResult<bool>> RevokeDelegationAsync(
            Guid delegationId,
            CancellationToken cancellationToken = default);

    Task<ServiceResult<string>> GetDelegationCredentialsAsync(
           Guid delegationId,
           CancellationToken cancellationToken = default);
}
