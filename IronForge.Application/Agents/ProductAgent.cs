using IronForge.Application.Agents.Models;
using IronForge.Application.Agents.Runtime;

namespace IronForge.Application.Agents;

/// <summary>
/// Entry point for the internal Product Agent. All behaviour lives in the
/// generic <see cref="IAgentRuntime"/>; the agent itself is configuration
/// (<see cref="AgentDefinitions.ProductAgent"/>).
/// </summary>
public class ProductAgent : IProductAgent
{
    private readonly IAgentRuntime _runtime;

    public ProductAgent(IAgentRuntime runtime)
    {
        _runtime = runtime;
    }

    public Task<AgentRunResult> RunAsync(
        Guid? sessionId,
        string userMessageText,
        CancellationToken cancellationToken = default)
    {
        return _runtime.RunAsync(
            AgentIds.ProductAgent,
            sessionId,
            userMessageText,
            cancellationToken);
    }

    public Task<AgentRunResult> RespondToApprovalAsync(
        string approvalId,
        bool approved,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        return _runtime.RespondToApprovalAsync(
            AgentIds.ProductAgent,
            approvalId,
            approved,
            reason,
            cancellationToken);
    }
}
