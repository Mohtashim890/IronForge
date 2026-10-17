using IronForge.Application.Agents.Models;
using IronForge.Application.Agents.Runtime;
using IronForge.Application.Agents.Tools;
using IronForge.Application.Execution;
using IronForge.Application.Tenants.Subscriptions;
using Microsoft.Extensions.AI;

namespace IronForge.Application.Agents.Governance;

/// <summary>
/// The agent tool catalogue as seen by an external (MCP) agent. Tools are the
/// same <see cref="IAgentTool"/>s the in-app agents use, built through the
/// same registry, so a tool added to the catalogue is available over MCP
/// without any MCP-specific code.
///
/// An external agent sees and may call a tool only when it is bound to the
/// agent, its module is in the tenant's subscription, the user delegated its
/// permission, and it does not need human approval (there is no approval flow
/// over MCP yet).
/// </summary>
public interface IExternalAgentToolService
{
    /// <summary>The catalogue entry for a tool, or null when it has none.</summary>
    AgentToolDescriptor? FindTool(string toolName);

    /// <summary>Governed functions the current external agent may call.</summary>
    Task<IReadOnlyList<AIFunction>> GetCallableToolsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Explains why a call is refused; checked before <see cref="InvokeAsync"/>.</summary>
    Task<(bool Allowed, string Reason)> AuthorizeAsync(
        string toolName,
        IDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the tool through its governed function: the governance policies
    /// are evaluated again on the actual arguments.
    /// </summary>
    Task<(bool Success, object? Result, string Error)> InvokeAsync(
        string toolName,
        IDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default);
}

public sealed class ExternalAgentToolService : IExternalAgentToolService
{
    private readonly IExecutionContextAccessor _executionContext;
    private readonly IAgentDefinitionProvider _definitions;
    private readonly IAgentToolRegistry _toolRegistry;
    private readonly IAgentGovernanceService _governance;
    private readonly ITenantEntitlementService _entitlements;

    public ExternalAgentToolService(
        IExecutionContextAccessor executionContext,
        IAgentDefinitionProvider definitions,
        IAgentToolRegistry toolRegistry,
        IAgentGovernanceService governance,
        ITenantEntitlementService entitlements)
    {
        _executionContext = executionContext;
        _definitions = definitions;
        _toolRegistry = toolRegistry;
        _governance = governance;
        _entitlements = entitlements;
    }

    public AgentToolDescriptor? FindTool(string toolName) =>
        _toolRegistry.FindDescriptor(toolName);

    public async Task<IReadOnlyList<AIFunction>> GetCallableToolsAsync(
        CancellationToken cancellationToken = default)
    {
        var agent = ExternalAgent();

        if (agent is null)
            return [];

        var definition = await TryGetDefinitionAsync(agent, cancellationToken);

        if (definition is null)
            return [];

        // Same construction as for the in-app model: bound tools of enabled
        // modules, limited to what the user (here: the delegated scopes) may
        // use. Approval-wrapped tools cannot run over MCP, so they are left out.
        return _toolRegistry
            .CreateModelTools(definition, agent)
            .Where(tool => tool is not ApprovalRequiredAIFunction)
            .OfType<AIFunction>()
            .ToList();
    }

    public async Task<(bool Allowed, string Reason)> AuthorizeAsync(
        string toolName,
        IDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var agent = ExternalAgent();

        if (agent is null)
            return (false, "External agent identity is not established.");

        var descriptor = _toolRegistry.FindDescriptor(toolName);

        if (descriptor is null)
            return (false, $"Tool '{toolName}' is not in the tool catalogue.");

        if (!await _entitlements.IsModuleEnabledAsync(descriptor.Module, cancellationToken))
            return (false, $"Tool '{toolName}' is not included in this workspace's subscription.");

        var definition = await TryGetDefinitionAsync(agent, cancellationToken);

        if (definition is null)
            return (false, $"Agent '{agent.AgentId}' does not exist or is not active.");

        if (definition.GetToolBinding(toolName) is null)
            return (false, $"Tool '{toolName}' is not enabled for agent '{agent.AgentId}'.");

        var decision = await _governance.EvaluateAsync(
            agent,
            _toolRegistry.GetEffectivePolicy(definition, toolName),
            new AgentGovernanceContext
            {
                ToolName = toolName,
                Arguments = arguments,
                ExecutionMode = AgentGovernanceExecutionMode.Normal
            },
            cancellationToken);

        return decision.Decision switch
        {
            AgentGovernanceDecisionType.Allow => (true, string.Empty),

            AgentGovernanceDecisionType.ApprovalRequired => (false,
                $"Tool '{toolName}' requires human approval, which is not available " +
                "over MCP. Use the agent in the IronForge app for this action."),

            _ => (false, decision.Reason)
        };
    }

    public async Task<(bool Success, object? Result, string Error)> InvokeAsync(
        string toolName,
        IDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var function = (await GetCallableToolsAsync(cancellationToken))
            .FirstOrDefault(x => string.Equals(x.Name, toolName, StringComparison.Ordinal));

        if (function is null)
            return (false, null, $"Tool '{toolName}' is not available to this agent.");

        try
        {
            var result = await function.InvokeAsync(
                new AIFunctionArguments(arguments),
                cancellationToken);

            return (true, result, string.Empty);
        }
        catch (UnauthorizedAccessException ex)
        {
            // Raised by GovernedAIFunction when a policy denies the call.
            return (false, null, ex.Message);
        }
    }

    private AgentIdentity? ExternalAgent()
    {
        var agent = _executionContext.Current?.Actor.Agent;

        return agent is { Type: AgentType.External } ? agent : null;
    }

    private async Task<AgentDefinition?> TryGetDefinitionAsync(
        AgentIdentity agent,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _definitions.GetRequiredAsync(agent.AgentId, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
