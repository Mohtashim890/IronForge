using IronForge.Application.Agents.Tools;

namespace IronForge.Application.Agents.Authorization;

/// <summary>
/// An agent's capability is defined by its tool bindings alone: it holds the
/// permission of every bound catalogue tool whose module the tenant has.
/// There is no separately edited permission list that could disagree.
/// </summary>
public static class AgentCapabilities
{
    public static IReadOnlySet<string> FromTools(
        IAgentToolRegistry toolRegistry,
        IEnumerable<string> boundToolNames,
        IReadOnlySet<string>? enabledModules)
    {
        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var toolName in boundToolNames)
        {
            // Null: the caller already limited the tools to enabled modules.
            if (toolRegistry.FindDescriptor(toolName) is { } descriptor &&
                (enabledModules is null || enabledModules.Contains(descriptor.Module)))
            {
                permissions.Add(descriptor.RequiredPermission);
            }
        }

        return permissions;
    }
}
