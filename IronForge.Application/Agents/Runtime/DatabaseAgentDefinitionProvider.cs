using IronForge.Application.Agents.Governance;
using IronForge.Application.Entities;
using IronForge.Application.Persistence;

namespace IronForge.Application.Agents.Runtime;

/// <summary>
/// Loads agent definitions from the tenant's Agent and AgentToolBinding rows.
/// Both repositories are tenant-filtered, so an agent can only be resolved
/// inside its own tenant. Inactive agents cannot run.
/// </summary>
public sealed class DatabaseAgentDefinitionProvider : IAgentDefinitionProvider
{
    private readonly IRepository<Agent> _agents;
    private readonly IRepository<AgentToolBindingEntity> _toolBindings;

    public DatabaseAgentDefinitionProvider(
        IRepository<Agent> agents,
        IRepository<AgentToolBindingEntity> toolBindings)
    {
        _agents = agents;
        _toolBindings = toolBindings;
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

        return new AgentDefinition
        {
            AgentId = agent.AgentId,
            Name = agent.Name,
            Version = agent.Version ?? "",
            Instructions = agent.Instructions,
            MaximumIterationsPerRequest = agent.MaximumIterationsPerRequest,
            AllowMultipleToolCalls = agent.AllowMultipleToolCalls,
            ApprovalLifetime = TimeSpan.FromMinutes(agent.ApprovalLifetimeMinutes),
            ModelProfile = agent.ModelProfile,

            // Insertion order is the order the tools are offered to the model.
            Tools = bindings
                .OrderBy(x => x.Id)
                .Select(ToBinding)
                .ToList()
        };
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
