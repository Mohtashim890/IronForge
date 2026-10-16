using IronForge.Application.Agents.Governance;
using IronForge.Application.Agents.Tools;
using IronForge.Application.Entities;
using IronForge.Application.Persistence;
using IronForge.Application.Tenants.Subscriptions;

namespace IronForge.Application.Agents.Runtime;

/// <summary>
/// Loads agent definitions from the tenant's Agent and AgentToolBinding rows.
/// Both repositories are tenant-filtered, so an agent can only be resolved
/// inside its own tenant. Inactive agents cannot run.
///
/// Bindings to tools whose module the tenant's subscription does not enable
/// are dropped here. The runtime and approved executions both load agents
/// through this provider, so after a downgrade such tools are neither offered
/// to the model nor executable, even for approvals that were already pending.
///
/// Likewise, an agent on a model profile the subscription does not include
/// runs on the subscription's default profile instead.
/// </summary>
public sealed class DatabaseAgentDefinitionProvider : IAgentDefinitionProvider
{
    private readonly IRepository<Agent> _agents;
    private readonly IRepository<AgentToolBindingEntity> _toolBindings;
    private readonly IAgentToolRegistry _toolRegistry;
    private readonly ITenantEntitlementService _entitlements;

    public DatabaseAgentDefinitionProvider(
        IRepository<Agent> agents,
        IRepository<AgentToolBindingEntity> toolBindings,
        IAgentToolRegistry toolRegistry,
        ITenantEntitlementService entitlements)
    {
        _agents = agents;
        _toolBindings = toolBindings;
        _toolRegistry = toolRegistry;
        _entitlements = entitlements;
    }

    public async Task<AgentDefinition> GetRequiredAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        var agent =
            await _agents.FirstOrDefaultAsync(
                x => x.AgentId == agentId && x.IsActive,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Agent '{agentId}' does not exist or is not active.");

        var bindings =
            await _toolBindings.ListAsync(
                x => x.AgentId == agent.Id,
                cancellationToken);

        var enabledModules =
            await _entitlements.GetEnabledModulesAsync(cancellationToken);

        // Tools missing from the catalogue are kept: the registry skips them
        // with a warning, which points at the stale binding.
        bindings = bindings
            .Where(x =>
                _toolRegistry.FindDescriptor(x.ToolName) is not { } descriptor ||
                enabledModules.Contains(descriptor.Module))
            .ToList();

        return new AgentDefinition
        {
            AgentId = agent.AgentId,
            Name = agent.Name,
            Version = agent.Version ?? "",
            Instructions = agent.Instructions,
            MaximumIterationsPerRequest = agent.MaximumIterationsPerRequest,
            AllowMultipleToolCalls = agent.AllowMultipleToolCalls,
            ApprovalLifetime = TimeSpan.FromMinutes(agent.ApprovalLifetimeMinutes),
            ModelProfile = await ResolveModelProfileAsync(agent.ModelProfile, cancellationToken),
            AllowedRoles = agent.AllowedRoles,

            // Insertion order is the order the tools are offered to the model.
            Tools = bindings
                .OrderBy(x => x.Id)
                .Select(ToBinding)
                .ToList()
        };
    }

    private async Task<string> ResolveModelProfileAsync(
        string modelProfile,
        CancellationToken cancellationToken)
    {
        var allowed = await _entitlements.GetAllowedModelProfilesAsync(cancellationToken);

        // No subscription information (e.g. an unknown plan): keep the agent's own.
        if (allowed.Count == 0 || allowed.Contains(modelProfile))
            return modelProfile;

        return allowed[0];
    }

    private static AgentToolBinding ToBinding(AgentToolBindingEntity entity)
    {
        return new AgentToolBinding
        {
            ToolName = entity.ToolName,
            RequiresApproval = entity.RequiresApproval,
            RiskLevel = Enum.TryParse<AgentRiskLevel>(
                entity.RiskLevel,
                ignoreCase: true,
                out var riskLevel)
                ? riskLevel
                : null,
            MaximumAmount = entity.MaximumAmount
        };
    }
}
