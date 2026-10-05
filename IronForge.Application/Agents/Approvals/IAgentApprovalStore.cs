using IronForge.Application.Entities;

namespace IronForge.Application.Agents.Approvals
{
    public interface IAgentApprovalStore
    {
        Task SaveAsync(
         AgentApproval approval,
         CancellationToken cancellationToken = default);

        Task<AgentApproval?> GetAsync(
            string approvalId,
            CancellationToken cancellationToken = default);

        Task UpdateAsync(
            AgentApproval approval,
            CancellationToken cancellationToken = default);
    }
}
