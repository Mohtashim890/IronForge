using IronForge.Application.Entities;
using IronForge.Application.Persistence;

namespace IronForge.Application.Agents.Authorization
{
    public sealed class DatabaseAgentPermissionRegistry
        : IAgentPermissionRegistry
    {
        private readonly IRepository<Agent> _agents;
        private readonly IRepository<AgentPermission> _permissions;

        public DatabaseAgentPermissionRegistry(
            IRepository<Agent> agents,
            IRepository<AgentPermission> permissions)
        {
            _agents = agents;
            _permissions = permissions;
        }

        public async Task<IReadOnlySet<string>> GetPermissionsAsync(
            string agentId,
            CancellationToken cancellationToken = default)
        {
            var agent = await _agents.FirstOrDefaultAsync(
                x =>
                    x.AgentId == agentId &&
                    x.IsActive,
                cancellationToken);

            if (agent is null)
            {
                return new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
            }

            var permissions =
                await _permissions.ListAsync(
                    x => x.AgentId == agent.Id,
                    cancellationToken);

            return permissions
                .Select(x => x.Permission)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);
        }
    }
}
