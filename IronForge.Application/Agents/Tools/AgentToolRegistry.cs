using IronForge.Shared.Subscriptions;
using IronForge.Application.Agents.Governance;
using IronForge.Application.Agents.Models;
using IronForge.Application.Agents.Runtime;
using IronForge.Application.Auth.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace IronForge.Application.Agents.Tools;

/// <summary>
/// Turns catalogue tools into governed functions for a specific agent.
///
/// Every function goes through the same pipeline:
///   catalogue tool → GovernedAIFunction (policies) → ApprovalRequiredAIFunction
///   (only for model calls whose effective policy requires approval).
/// Both the approval wrapper and the governance checks read the same
/// effective policy, so they cannot disagree.
///
/// The model is only offered tools the current user may use. Governance
/// (UserPermissionPolicy) enforces the same rule at execution; filtering here
/// stops the model from proposing actions, including approval requests, that
/// would be denied anyway.
/// </summary>
public sealed class AgentToolRegistry : IAgentToolRegistry
{
    private readonly IReadOnlyDictionary<string, IAgentTool> _tools;
    private readonly IAgentGovernanceService _governance;
    private readonly IUserAuthorizationService _userAuthorization;
    private readonly ILogger<AgentToolRegistry> _logger;

    public AgentToolRegistry(
        IEnumerable<IAgentTool> tools,
        IAgentGovernanceService governance,
        IUserAuthorizationService userAuthorization,
        ILogger<AgentToolRegistry> logger)
    {
        var catalogue =
            new Dictionary<string, IAgentTool>(StringComparer.Ordinal);

        foreach (var tool in tools)
        {
            if (PlatformModules.Find(tool.Descriptor.Module) is null)
            {
                throw new InvalidOperationException(
                    $"Agent tool '{tool.Descriptor.Name}' belongs to unknown module " +
                    $"'{tool.Descriptor.Module}'. Add it to PlatformModules.All.");
            }

            if (!catalogue.TryAdd(tool.Descriptor.Name, tool))
            {
                throw new InvalidOperationException(
                    $"Agent tool '{tool.Descriptor.Name}' is registered more than once.");
            }
        }

        _tools = catalogue;
        _governance = governance;
        _userAuthorization = userAuthorization;
        _logger = logger;
    }

    public IReadOnlyList<AgentToolDescriptor> GetCatalogue()
    {
        return _tools.Values
            .Select(tool => tool.Descriptor)
            .OrderBy(descriptor => descriptor.Name, StringComparer.Ordinal)
            .ToList();
    }

    public AgentToolDescriptor? FindDescriptor(string toolName)
    {
        return _tools.TryGetValue(toolName, out var tool)
            ? tool.Descriptor
            : null;
    }

    public AgentToolPolicy GetEffectivePolicy(
        AgentDefinition definition,
        string toolName)
    {
        var binding =
            definition.GetToolBinding(toolName)
            ?? throw new InvalidOperationException(
                $"Agent '{definition.AgentId}' is not allowed to use tool '{toolName}'.");

        var descriptor = GetTool(toolName).Descriptor;

        return new AgentToolPolicy
        {
            ToolName = descriptor.Name,
            RequiredPermission = descriptor.RequiredPermission,

            // Overrides may only tighten the catalogue defaults.
            RequiresApproval =
                descriptor.RequiresApproval || binding.RequiresApproval == true,

            RiskLevel =
                binding.RiskLevel is { } risk && risk > descriptor.RiskLevel
                    ? risk
                    : descriptor.RiskLevel,

            MaximumAmount =
                Lowest(descriptor.MaximumAmount, binding.MaximumAmount)
        };
    }

    public IList<AITool> CreateModelTools(
        AgentDefinition definition,
        AgentIdentity agent)
    {
        var tools = new List<AITool>();

        foreach (var binding in definition.Tools)
        {
            if (!_tools.ContainsKey(binding.ToolName))
            {
                // A stored binding can outlive a tool removed from the code.
                // Fail closed for that tool only, so the agent keeps working
                // with the rest of its tools.
                _logger.LogWarning(
                    "Agent {AgentId} is bound to tool {ToolName}, which is not in " +
                    "the tool catalogue. The tool is not offered to the model.",
                    definition.AgentId,
                    binding.ToolName);

                continue;
            }

            var policy = GetEffectivePolicy(definition, binding.ToolName);

            if (!_userAuthorization.HasPermission(policy.RequiredPermission))
            {
                // Not the user's to delegate (e.g. a Viewer and create_product).
                continue;
            }

            AIFunction function = Govern(
                binding.ToolName,
                agent,
                policy,
                AgentGovernanceExecutionMode.Normal);

            if (policy.RequiresApproval)
            {
                // The model's call is turned into an approval request
                // instead of being executed.
                function = new ApprovalRequiredAIFunction(function);
            }

            tools.Add(function);
        }

        return tools;
    }

    public AIFunction CreateApprovedExecutionTool(
        AgentDefinition definition,
        AgentIdentity agent,
        string toolName)
    {
        var policy = GetEffectivePolicy(definition, toolName);

        return Govern(
            toolName,
            agent,
            policy,
            AgentGovernanceExecutionMode.ApprovedExecution);
    }

    private GovernedAIFunction Govern(
        string toolName,
        AgentIdentity agent,
        AgentToolPolicy policy,
        AgentGovernanceExecutionMode executionMode)
    {
        return new GovernedAIFunction(
            GetTool(toolName).CreateFunction(),
            agent,
            policy,
            executionMode,
            _governance);
    }

    private IAgentTool GetTool(string toolName)
    {
        return _tools.TryGetValue(toolName, out var tool)
            ? tool
            : throw new InvalidOperationException(
                $"Agent tool '{toolName}' is not registered in the tool catalogue.");
    }

    private static decimal? Lowest(decimal? first, decimal? second)
    {
        if (first is null) return second;
        if (second is null) return first;
        return Math.Min(first.Value, second.Value);
    }
}
