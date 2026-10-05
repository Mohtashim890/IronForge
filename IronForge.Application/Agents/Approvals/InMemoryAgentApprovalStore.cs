using IronForge.Application.Entities;
using System.Collections.Concurrent;

namespace IronForge.Application.Agents.Approvals;

public class InMemoryAgentApprovalStore
    : IAgentApprovalStore
{
    private readonly ConcurrentDictionary<
        string,
        AgentApproval> _approvals = new();

    public Task SaveAsync(
        AgentApproval approval,
        CancellationToken cancellationToken = default)
    {
        _approvals[approval.ApprovalId] = approval;

        return Task.CompletedTask;
    }

    public Task<AgentApproval?> GetAsync(
        string approvalId,
        CancellationToken cancellationToken = default)
    {
        _approvals.TryGetValue(
            approvalId,
            out var approval);

        return Task.FromResult(approval);
    }

    public Task UpdateAsync(
     AgentApproval approval,
     CancellationToken cancellationToken = default)
    {
        _approvals[approval.ApprovalId] = approval;

        return Task.CompletedTask;
    }
}