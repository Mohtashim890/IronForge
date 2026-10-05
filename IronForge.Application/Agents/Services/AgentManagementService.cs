using IronForge.Application.Agents.DTOs;
using IronForge.Application.Commons;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.Persistence;
using System.Net;

namespace IronForge.Application.Agents.Services
{
    public sealed class AgentManagementService
        : IAgentManagementService
    {
        private readonly IRepository<Agent> _agents;
        private readonly IRepository<AgentPermission> _permissions;
        private readonly IExecutionContextAccessor _executionContextAccessor;
        private readonly IAgentManagementAuthorizationService _authorization;

        public AgentManagementService(
            IRepository<Agent> agents,
            IRepository<AgentPermission> permissions,
            IExecutionContextAccessor executionContextAccessor,
            IAgentManagementAuthorizationService agentManagementAuthorizationService)
        {
            _agents = agents;
            _permissions = permissions;
            _executionContextAccessor = executionContextAccessor;
            _authorization = agentManagementAuthorizationService;
        }

        public async Task<ServiceResult<List<AgentDto>>> GetAgentsAsync(
            CancellationToken cancellationToken = default)
        {
            var authorization =
                await _authorization.AuthorizeAsync(
                    Permissions.AgentsRead,
                    cancellationToken);

            if (!authorization.Allowed)
            {
                return ServiceResult<List<AgentDto>>.Fail(
                    authorization.Reason ??
                    "You are not authorized to view agents.",
                    403);
            }

            var agents =
                await _agents.ListAsync(cancellationToken);

            var agentIds = agents
                .Select(x => x.Id)
                .ToList();

            var permissions = agentIds.Count == 0
                ? new List<AgentPermission>()
                : await _permissions.ListAsync(
                    x => agentIds.Contains(x.AgentId),
                    cancellationToken);

            var permissionsByAgent = permissions
                .GroupBy(x => x.AgentId)
                .ToDictionary(
                    x => x.Key,
                    x => x
                        .Select(p => p.Permission)
                        .OrderBy(p => p)
                        .ToList());

            var result =
                agents
                    .OrderBy(x => x.Name)
                    .Select(agent =>
                        Map(
                            agent,
                            permissionsByAgent.TryGetValue(
                                agent.Id,
                                out var agentPermissions)
                                ? agentPermissions
                                : []))
                    .ToList();

            return ServiceResult<List<AgentDto>>.Ok(result);
        }

        public async Task<ServiceResult<AgentDto>> GetAgentAsync(
            string agentId,
            CancellationToken cancellationToken = default)
        {
            var authorization =
                await _authorization.AuthorizeAsync(
                    Permissions.AgentsRead,
                    cancellationToken);

            if (!authorization.Allowed)
            {
                return ServiceResult<AgentDto>.Fail(
                    authorization.Reason ??
                    "You are not authorized to view this agent.",
                    403);
            }

            if (string.IsNullOrWhiteSpace(agentId))
            {
                return ServiceResult<AgentDto>.Fail(
                    "Agent ID is required.",
                    400);
            }

            var agent =
                await _agents.FirstOrDefaultAsync(
                    x => x.AgentId == agentId,
                    cancellationToken);

            if (agent is null)
            {
                return ServiceResult<AgentDto>.Fail(
                    "Agent not found.",
                    404);
            }

            var permissions =
                await _permissions.ListAsync(
                    x => x.AgentId == agent.Id,
                    cancellationToken);

            return ServiceResult<AgentDto>.Ok(
                Map(
                    agent,
                    permissions
                        .Select(x => x.Permission)
                        .OrderBy(x => x)
                        .ToList()));
        }

        public async Task<ServiceResult<AgentDto>> CreateAgentAsync(
            CreateAgentRequest request,
            CancellationToken cancellationToken = default)
        {
            var authorization =
               await _authorization.AuthorizeAsync(
                   Permissions.AgentsCreate,
                   cancellationToken);

            if (!authorization.Allowed)
            {
                return ServiceResult<AgentDto>.Fail(
                    authorization.Reason ??
                    "You are not authorized to create agents.",
                    403);
            }

            var normalizedAgentId =
                request.AgentId.Trim();

            if (string.IsNullOrWhiteSpace(normalizedAgentId))
            {
                return ServiceResult<AgentDto>.Fail(
                    "Agent ID is required.",
                    400);
            }

            var exists =
                await _agents.AnyAsync(
                    x => x.AgentId == normalizedAgentId,
                    cancellationToken);

            if (exists)
            {
                return ServiceResult<AgentDto>.Fail(
                    "An agent with this ID already exists.",
                    409);
            }

            var agent = new Agent
            {
                TenantId = ResolveTenantId(),
                AgentId = normalizedAgentId,
                Name = request.Name.Trim(),
                Description = request.Description?.Trim(),
                Version = request.Version.Trim(),
                IsActive = request.IsActive,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            _agents.Add(agent);

            await _agents.SaveChangesAsync(
                cancellationToken);

            await ReplacePermissionsAsync(
                agent,
                request.Permissions,
                cancellationToken);

            var permissions =
                await _permissions.ListAsync(
                    x => x.AgentId == agent.Id,
                    cancellationToken);

            return ServiceResult<AgentDto>.Ok(
                Map(
                    agent,
                    permissions
                        .Select(x => x.Permission)
                        .OrderBy(x => x)
                        .ToList()));
        }

        public async Task<ServiceResult<AgentDto>> UpdateAgentAsync(
            string agentId,
            UpdateAgentRequest request,
            CancellationToken cancellationToken = default)
        {
            var authorization =
                await _authorization.AuthorizeAsync(
                    Permissions.AgentsUpdate,
                    cancellationToken);

            if (!authorization.Allowed)
            {
                return ServiceResult<AgentDto>.Fail(
                    authorization.Reason ??
                    "You are not authorized to update agents.",
                    403);
            }

            var agent =
                await _agents.FirstOrDefaultTrackedAsync(
                    x => x.AgentId == agentId,
                    cancellationToken);

            if (agent is null)
            {
                return ServiceResult<AgentDto>.Fail(
                    "Agent not found.",
                    404);
            }

            agent.Name =
                request.Name.Trim();

            agent.Description =
                string.IsNullOrEmpty(request.Description)
                    ? agent.Description
                    : request.Description?.Trim();

            agent.Version =
                request.Version.Trim();

            agent.IsActive = request.IsActive;

            agent.UpdatedAtUtc =
                DateTime.UtcNow;

            //_agents.Update(agent);

            await _agents.SaveChangesAsync(
                cancellationToken);

            var permissions =
                await _permissions.ListAsync(
                    x => x.AgentId == agent.Id,
                    cancellationToken);

            return ServiceResult<AgentDto>.Ok(
                Map(
                    agent,
                    permissions
                        .Select(x => x.Permission)
                        .OrderBy(x => x)
                        .ToList()));
        }

        public async Task<ServiceResult<bool>> DeleteAgentAsync(
            string agentId,
            CancellationToken cancellationToken = default)
        {
            var authorization =
               await _authorization.AuthorizeAsync(
                   Permissions.AgentsDelete,
                   cancellationToken);

            if (!authorization.Allowed)
            {
                return ServiceResult<bool>.Fail(
                    authorization.Reason ??
                    "You are not authorized to delete agents.",
                    403);
            }

            var agent =
                await _agents.FirstOrDefaultAsync(
                    x => x.AgentId == agentId,
                    cancellationToken);

            if (agent is null)
            {
                return ServiceResult<bool>.Fail(
                    "Agent not found.",
                    404);
            }

            _agents.Remove(agent);

            await _agents.SaveChangesAsync(
                cancellationToken);

            return ServiceResult<bool>.Ok(true);
        }

        public async Task<ServiceResult<AgentDto>> UpdatePermissionsAsync(
            string agentId,
            UpdateAgentPermissionsRequest request,
            CancellationToken cancellationToken = default)
        {
            var authorization =
                await _authorization.AuthorizeAsync(
                    Permissions.AgentsPermissionsManage,
                    cancellationToken);

            if (!authorization.Allowed)
            {
                return ServiceResult<AgentDto>.Fail(
                    authorization.Reason ??
                    "You are not authorized to manage agent permissions.",
                    403);
            }

            var agent =
                await _agents.FirstOrDefaultAsync(
                    x => x.AgentId == agentId,
                    cancellationToken);

            if (agent is null)
            {
                return ServiceResult<AgentDto>.Fail(
                    "Agent not found.",
                    404);
            }

            await ReplacePermissionsAsync(
                agent,
                request.Permissions,
                cancellationToken);

            var permissions =
                await _permissions.ListAsync(
                    x => x.AgentId == agent.Id,
                    cancellationToken);

            return ServiceResult<AgentDto>.Ok(
                Map(
                    agent,
                    permissions
                        .Select(x => x.Permission)
                        .OrderBy(x => x)
                        .ToList()));
        }

        private async Task ReplacePermissionsAsync(
            Agent agent,
            IEnumerable<string> permissions,
            CancellationToken cancellationToken)
        {
            var normalizedPermissions =
                permissions
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            var existingPermissions =
                await _permissions.ListAsync(
                    x => x.AgentId == agent.Id,
                    cancellationToken);

            foreach (var permission in existingPermissions)
            {
                _permissions.Remove(permission);
            }

            foreach (var permission in normalizedPermissions)
            {
                _permissions.Add(
                    new AgentPermission
                    {
                        AgentId = agent.Id,
                        Permission = permission
                    });
            }

            agent.UpdatedAtUtc =
                DateTime.UtcNow;

            //_agents.Update(agent);

            await _agents.SaveChangesAsync(
                cancellationToken);
        }

        private int ResolveTenantId()
        {
            return _executionContextAccessor.Current?.TenantId ??
            throw new InvalidOperationException(
                "Agent creation requires an established tenant execution context.");
        }

        private static AgentDto Map(
            Agent agent,
            IEnumerable<string> permissions)
        {
            return new AgentDto
            {
                Id = agent.Id,
                AgentId = agent.AgentId,
                Name = agent.Name,
                Description = agent.Description,
                Version = agent.Version,
                IsActive = agent.IsActive,
                CreatedAtUtc = agent.CreatedAtUtc,
                UpdatedAtUtc = agent.UpdatedAtUtc,
                Permissions =
                    permissions
                        .OrderBy(x => x)
                        .ToList()
            };
        }
    }
}
