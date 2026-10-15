namespace IronForge.Application.Agents.Runtime;

public interface IAgentDefinitionProvider
{
    /// <summary>
    /// Returns the definition for <paramref name="agentId"/> in the current
    /// tenant. Throws when the agent does not exist or is inactive.
    /// </summary>
    Task<AgentDefinition> GetRequiredAsync(
        string agentId,
        CancellationToken cancellationToken = default);
}
