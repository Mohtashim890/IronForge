using IronForge.Shared.Authorization;
using IronForge.Application.Tenants.Subscriptions;
using IronForge.Shared.Subscriptions;
using IronForge.Application.Agents.Authorization;
using IronForge.Application.Agents.DTOs;
using IronForge.Application.Agents.Governance;
using IronForge.Application.Agents.Runtime;
using IronForge.Application.Agents.ModelProfiles;
using IronForge.Application.Agents.Usage;
using IronForge.Application.Agents.Tools;
using IronForge.Application.Commons;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.Persistence;
using IronForge.Shared.Models.Agents;
using IronForge.Shared.Models.Agents.Manage;
using System.Net;
using AgentDto = IronForge.Application.Agents.DTOs.AgentDto;

namespace IronForge.Application.Agents.Services
{
    public sealed class AgentManagementService
        : IAgentManagementService
    {
        private readonly IRepository<Agent> _agents;
        private readonly IRepository<AgentToolBindingEntity> _toolBindings;
        private readonly IAgentToolRegistry _toolRegistry;
        private readonly IAgentQuotaService _quota;
        private readonly IExecutionContextAccessor _executionContextAccessor;
        private readonly IAgentManagementAuthorizationService _authorization;
        private readonly ITenantEntitlementService _entitlements;

        public AgentManagementService(
            IRepository<Agent> agents,
            IRepository<AgentToolBindingEntity> toolBindings,
            IAgentToolRegistry toolRegistry,
            IAgentQuotaService quota,
            IExecutionContextAccessor executionContextAccessor,
            IAgentManagementAuthorizationService agentManagementAuthorizationService,
            ITenantEntitlementService entitlements)
        {
            _agents = agents;
            _toolBindings = toolBindings;
            _toolRegistry = toolRegistry;
            _quota = quota;
            _executionContextAccessor = executionContextAccessor;
            _authorization = agentManagementAuthorizationService;
            _entitlements = entitlements;
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

            var toolBindings = agentIds.Count == 0
                ? new List<AgentToolBindingEntity>()
                : await _toolBindings.ListAsync(
                    x => agentIds.Contains(x.AgentId),
                    cancellationToken);

            var toolsByAgent = toolBindings
                .GroupBy(x => x.AgentId)
                .ToDictionary(x => x.Key, x => x.ToList());

            var enabledModules =
                await _entitlements.GetEnabledModulesAsync(cancellationToken);

            var result =
                agents
                    .OrderBy(x => x.Name)
                    .Select(agent =>
                        Map(
                            agent,
                            toolsByAgent.TryGetValue(
                                agent.Id,
                                out var agentTools)
                                ? agentTools
                                : [],
                            enabledModules))
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

            return ServiceResult<AgentDto>.Ok(
                await MapAsync(
                    agent,
                    cancellationToken));
        }

        /// <summary>
        /// Turns a built-in agent on or off for the gym. Its instructions and
        /// tools come from the platform template and are not editable here.
        /// </summary>
        public async Task<ServiceResult<AgentDto>> SetActiveAsync(
            string agentId,
            bool isActive,
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

            agent.IsActive = isActive;
            agent.UpdatedAtUtc = DateTime.UtcNow;

            await _agents.SaveChangesAsync(cancellationToken);

            return ServiceResult<AgentDto>.Ok(
                await MapAsync(
                    agent,
                    cancellationToken));
        }

        public async Task<ServiceResult<AgentUsageSummaryDto>> GetUsageAsync(
            CancellationToken cancellationToken = default)
        {
            var authorization =
                await _authorization.AuthorizeAsync(
                    Permissions.AgentsRead,
                    cancellationToken);

            if (!authorization.Allowed)
            {
                return ServiceResult<AgentUsageSummaryDto>.Fail(
                    authorization.Reason ??
                    "You are not authorized to view agent usage.",
                    403);
            }

            return ServiceResult<AgentUsageSummaryDto>.Ok(
                await _quota.GetSummaryAsync(cancellationToken));
        }

        /// <summary>
        /// Active agents any signed-in member of the tenant may chat with.
        /// Deliberately minimal (no instructions, tools or permissions), and
        /// not gated by agents.read: using an agent is not administering it.
        /// What the agent can then do is still limited by the user's own
        /// permissions (user ∩ agent).
        /// </summary>
        public async Task<ServiceResult<List<AvailableAgentDto>>> GetAvailableAgentsAsync(
            CancellationToken cancellationToken = default)
        {
            var agents =
                await _agents.ListAsync(
                    x => x.IsActive,
                    cancellationToken);

            var role = _executionContextAccessor.Current?.Actor.Role ?? "";

            // A template agent whose modules the subscription no longer covers
            // (e.g. the coach after a downgrade) cannot run, so it is not offered.
            var enabledModules =
                await _entitlements.GetEnabledModulesAsync(cancellationToken);

            return ServiceResult<List<AvailableAgentDto>>.Ok(
                agents
                    .Where(x =>
                        x.AllowedRoles.Count == 0 ||
                        x.AllowedRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
                    .Where(x =>
                        AgentTemplates.Find(x.TemplateId) is not { } template ||
                        template.IsCoveredBy(enabledModules))
                    .OrderBy(x => x.Name)
                    .Select(x => new AvailableAgentDto
                    {
                        AgentId = x.AgentId,
                        Name = x.Name,
                        Description = x.Description
                    })
                    .ToList());
        }

        private async Task<AgentDto> MapAsync(
            Agent agent,
            CancellationToken cancellationToken)
        {
            var toolBindings =
                await _toolBindings.ListAsync(
                    x => x.AgentId == agent.Id,
                    cancellationToken);

            return Map(
                agent,
                toolBindings,
                await _entitlements.GetEnabledModulesAsync(cancellationToken));
        }

        private AgentDto Map(
            Agent agent,
            IReadOnlyList<AgentToolBindingEntity> toolBindings,
            IReadOnlySet<string> enabledModules)
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
                // Derived, read-only: the permissions of the agent's tools.
                Permissions =
                    AgentCapabilities.FromTools(
                            _toolRegistry,
                            toolBindings.Select(x => x.ToolName),
                            enabledModules)
                        .Order(StringComparer.Ordinal)
                        .ToList(),
                Instructions = agent.Instructions,
                MaximumIterationsPerRequest = agent.MaximumIterationsPerRequest,
                AllowMultipleToolCalls = agent.AllowMultipleToolCalls,
                ApprovalLifetimeMinutes = agent.ApprovalLifetimeMinutes,
                ModelProfile = agent.ModelProfile,
                AllowedRoles = agent.AllowedRoles.ToList(),
                IsManaged = agent.IsManaged,
                TemplateVersion = agent.TemplateVersion,
                Tools =
                    toolBindings
                        .OrderBy(x => x.Id)
                        .Select(x => new AgentToolBindingDto
                        {
                            ToolName = x.ToolName,
                            RequiresApproval = x.RequiresApproval,
                            RiskLevel = x.RiskLevel,
                            MaximumAmount = x.MaximumAmount
                        })
                        .ToList()
            };
        }
    }
}
