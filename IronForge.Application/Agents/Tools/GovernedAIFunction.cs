using IronForge.Application.Agents.Governance;
using IronForge.Application.Agents.Models;
using Microsoft.Extensions.AI;
using System.Diagnostics;

namespace IronForge.Application.Agents.Tools;

/// <summary>
/// Wraps a catalogue tool so that every invocation is evaluated by the
/// governance policies (permission, approval, amount limits) using the
/// tool's effective policy and the actual call arguments.
///
/// Deny always blocks. ApprovalRequired only passes when the call is being
/// executed after a human approval; otherwise it is blocked, so a tool
/// that needs approval can never run without one, even if it were offered
/// to a model without the approval wrapper.
/// </summary>
public sealed class GovernedAIFunction : DelegatingAIFunction
{
    private readonly AgentIdentity _agent;
    private readonly AgentToolPolicy _policy;
    private readonly AgentGovernanceExecutionMode _executionMode;
    private readonly IAgentGovernanceService _governance;

    public GovernedAIFunction(
        AIFunction innerFunction,
        AgentIdentity agent,
        AgentToolPolicy policy,
        AgentGovernanceExecutionMode executionMode,
        IAgentGovernanceService governance)
        : base(innerFunction)
    {
        _agent = agent;
        _policy = policy;
        _executionMode = executionMode;
        _governance = governance;
    }

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var decision =
            await _governance.EvaluateAsync(
                _agent,
                _policy,
                new AgentGovernanceContext
                {
                    ToolName = _policy.ToolName,
                    Arguments = new Dictionary<string, object?>(arguments),
                    ExecutionMode = _executionMode
                },
                cancellationToken);

        Activity.Current?.SetTag(
            "ironforge.tool.governance_decision",
            decision.Decision.ToString());

        if (decision.Decision == AgentGovernanceDecisionType.Deny)
        {
            throw new UnauthorizedAccessException(decision.Reason);
        }

        if (decision.Decision == AgentGovernanceDecisionType.ApprovalRequired &&
            _executionMode != AgentGovernanceExecutionMode.ApprovedExecution)
        {
            throw new UnauthorizedAccessException(
                $"Tool '{_policy.ToolName}' requires human approval.");
        }

        return await base.InvokeCoreAsync(arguments, cancellationToken);
    }
}
