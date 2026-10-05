using IronForge.Application.Entities;

namespace IronForge.Application.Agents.Approvals
{
    public interface IApprovedToolExecutionService
    {
        Task<object?> ExecuteAsync(
         AgentApproval approval,
         CancellationToken cancellationToken = default);
    }
}
