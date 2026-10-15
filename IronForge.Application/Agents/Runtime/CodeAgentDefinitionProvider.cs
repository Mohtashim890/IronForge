using Microsoft.Extensions.DependencyInjection;

namespace IronForge.Application.Agents.Runtime;

/// <summary>
/// In-memory provider over definitions declared in code (by default the
/// <see cref="AgentDefinitions"/> templates). The API uses
/// <see cref="DatabaseAgentDefinitionProvider"/>; this provider is for tests
/// and tooling that need definitions without a database.
/// </summary>
public sealed class CodeAgentDefinitionProvider : IAgentDefinitionProvider
{
    private readonly IReadOnlyDictionary<string, AgentDefinition> _definitions;

    public CodeAgentDefinitionProvider()
        : this(AgentDefinitions.All)
    {
    }

    public CodeAgentDefinitionProvider(
        IEnumerable<AgentDefinition> definitions)
    {
        _definitions = definitions.ToDictionary(
            definition => definition.AgentId,
            StringComparer.Ordinal);
    }

    public Task<AgentDefinition> GetRequiredAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        if (_definitions.TryGetValue(agentId, out var definition))
        {
            return Task.FromResult(definition);
        }

        return Task.FromException<AgentDefinition>(
            new InvalidOperationException(
                $"No agent definition exists for '{agentId}'."));
    }
}
