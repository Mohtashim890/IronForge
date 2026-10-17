using IronForge.Application.Agents.Runtime;
using IronForge.Application.Agents.Tools;

namespace IronForge.Application.Agents.Authorization
{
    /// <summary>
    /// Permissions of an agent, derived from its tool bindings
    /// (<see cref="AgentCapabilities"/>). The definition provider is
    /// tenant-filtered and drops tools of disabled modules, so an agent of
    /// another tenant, an inactive agent or a downgraded tool yields nothing.
    /// This is the ceiling for delegation scopes.
    ///
    /// The dependencies are resolved on first use, not at construction:
    /// catalogue tools depend on domain services whose authorization depends
    /// on this registry, so taking the tool registry directly would be a
    /// dependency cycle. By the time permissions are asked for, those
    /// services already exist in the request scope.
    /// </summary>
    public sealed class DatabaseAgentPermissionRegistry
        : IAgentPermissionRegistry
    {
        private static readonly IReadOnlySet<string> None =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly Func<IAgentDefinitionProvider> _definitions;
        private readonly Func<IAgentToolRegistry> _toolRegistry;

        public DatabaseAgentPermissionRegistry(
            Func<IAgentDefinitionProvider> definitions,
            Func<IAgentToolRegistry> toolRegistry)
        {
            _definitions = definitions;
            _toolRegistry = toolRegistry;
        }

        public async Task<IReadOnlySet<string>> GetPermissionsAsync(
            string agentId,
            CancellationToken cancellationToken = default)
        {
            AgentDefinition definition;

            try
            {
                definition = await _definitions().GetRequiredAsync(
                    agentId,
                    cancellationToken);
            }
            catch (InvalidOperationException)
            {
                return None;
            }

            // Bindings are already limited to enabled modules by the provider.
            return AgentCapabilities.FromTools(
                _toolRegistry(),
                definition.Tools.Select(x => x.ToolName),
                enabledModules: null);
        }
    }
}
