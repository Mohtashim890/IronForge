using IronForge.Application.Agents.Models;

namespace IronForge.Application.Agents
{
    public interface IProductAgent
    {
        Task<AgentRunResult> RunAsync(
     Guid? sessionId,
     string message,
     CancellationToken cancellationToken = default);
        Task<AgentRunResult> RespondToApprovalAsync(
        string approvalId,
        bool approved,
        string? reason = null,
        CancellationToken cancellationToken = default);
    }
}
