using IronForge.Application.Agents.Models;

namespace IronForge.Application.Agents.Runtime;

/// <summary>
/// Describes an internal agent as data: who it is, how it should behave,
/// which tools it may use and the limits of a single run.
///
/// The <see cref="AgentRuntime"/> executes any definition; nothing in the
/// runtime is specific to one agent. Definitions are currently provided from
/// code (<see cref="CodeAgentDefinitionProvider"/>) and can later be loaded
/// from the database per tenant without changing the runtime.
/// </summary>
public sealed class AgentDefinition
{
    public required string AgentId { get; init; }

    public required string Name { get; init; }

    public required string Version { get; init; }

    /// <summary>
    /// System prompt sent as the first message of every model call.
    /// It is never persisted in the conversation history.
    /// </summary>
    public required string Instructions { get; init; }

    /// <summary>
    /// Catalogue tools the agent may use, with optional governance overrides.
    /// The effective policy for each tool is computed by the tool registry
    /// from the catalogue defaults and these bindings.
    /// </summary>
    public required IReadOnlyList<AgentToolBinding> Tools { get; init; }

    public int MaximumIterationsPerRequest { get; init; } = 5;

    public bool AllowMultipleToolCalls { get; init; }

    public TimeSpan ApprovalLifetime { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Platform model profile the agent runs on.</summary>
    public string ModelProfile { get; init; } = "standard";

    /// <summary>Tenant roles that may chat with the agent; empty means everyone.</summary>
    public IReadOnlyList<string> AllowedRoles { get; init; } = [];

    public bool AllowsRole(string? role) =>
        AllowedRoles.Count == 0 ||
        AllowedRoles.Contains(role ?? "", StringComparer.OrdinalIgnoreCase);

    public AgentToolBinding? GetToolBinding(string toolName)
    {
        return Tools.FirstOrDefault(
            tool => string.Equals(
                tool.ToolName,
                toolName,
                StringComparison.Ordinal));
    }

    public AgentIdentity CreateIdentity()
    {
        return new AgentIdentity
        {
            AgentId = AgentId,
            Name = Name,
            Version = Version,
            Type = AgentType.Internal
        };
    }
}
