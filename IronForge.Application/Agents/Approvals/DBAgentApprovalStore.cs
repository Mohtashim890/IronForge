using IronForge.Application.Entities;
using IronForge.Application.Persistence;

namespace IronForge.Application.Agents.Approvals;

public class DBAgentApprovalStore : IAgentApprovalStore
{
    private readonly IRepository<AgentApproval> _approvals;

    public DBAgentApprovalStore(
        IRepository<AgentApproval> approvals)
    {
        _approvals = approvals;
    }

    public async Task SaveAsync(
        AgentApproval approval,
        CancellationToken cancellationToken = default)
    {
        _approvals.Add(approval);

        await _approvals.SaveChangesAsync(
            cancellationToken);
    }

    public async Task<AgentApproval?> GetAsync(
        string approvalId,
        CancellationToken cancellationToken = default)
    {
        return await _approvals.FirstOrDefaultAsync(
            x => x.ApprovalId == approvalId,
            cancellationToken);
    }

    public async Task UpdateAsync(
        AgentApproval approval,
        CancellationToken cancellationToken = default)
    {
        //_approvals.Update(approval);

        await _approvals.SaveChangesAsync(
            cancellationToken);
    }
}
