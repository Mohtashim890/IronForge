using IronForge.Application.Entities;

namespace IronForge.Application.Agents.Approvals
{
    public interface IApprovalExecutionAuthorizationService
    {
        Task<ApprovalExecutionAuthorizationResult> AuthorizeAsync(
       AgentApproval approval,
       CancellationToken cancellationToken = default);
    }
}
