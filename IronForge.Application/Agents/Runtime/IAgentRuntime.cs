using IronForge.Application.Agents.Models;

namespace IronForge.Application.Agents.Runtime;

/// <summary>
/// Executes any internal agent described by an <see cref="AgentDefinition"/>.
/// </summary>
public interface IAgentRuntime
{
    Task<AgentRunResult> RunAsync(
        string agentId,
        Guid? sessionId,
        string userMessageText,
        CancellationToken cancellationToken = default);

    Task<AgentRunResult> RespondToApprovalAsync(
        string agentId,
        string approvalId,
        bool approved,
        string? reason = null,
        CancellationToken cancellationToken = default);
}
