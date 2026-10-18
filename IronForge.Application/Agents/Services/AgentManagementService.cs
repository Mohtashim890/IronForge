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
using CreateAgentRequest = IronForge.Application.Agents.DTOs.CreateAgentRequest;
using UpdateAgentRequest = IronForge.Application.Agents.DTOs.UpdateAgentRequest;

namespace IronForge.Application.Agents.Services
{
    public sealed class AgentManagementService
        : IAgentManagementService
    {
        private readonly IRepository<Agent> _agents;
        private readonly IRepository<AgentToolBindingEntity> _toolBindings;
        private readonly IAgentToolRegistry _toolRegistry;
        private readonly IAgentChatClientFactory _modelProfiles;
        private readonly IAgentQuotaService _quota;
        private readonly IExecutionContextAccessor _executionContextAccessor;
        private readonly IAgentManagementAuthorizationService _authorization;
        private readonly ITenantEntitlementService _entitlements;

        public AgentManagementService(
            IRepository<Agent> agents,
            IRepository<AgentToolBindingEntity> toolBindings,
            IAgentToolRegistry toolRegistry,
            IAgentChatClientFactory modelProfiles,
            IAgentQuotaService quota,
            IExecutionContextAccessor executionContextAccessor,
            IAgentManagementAuthorizationService agentManagementAuthorizationService,
            ITenantEntitlementService entitlements)
        {
            _agents = agents;
            _toolBindings = toolBindings;
            _toolRegistry = toolRegistry;
            _modelProfiles = modelProfiles;
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

            // Default: the subscription's first profile.
            var modelProfile =
                string.IsNullOrWhiteSpace(request.ModelProfile)
                    ? (await _entitlements.GetAllowedModelProfilesAsync(cancellationToken))
                        .FirstOrDefault(_modelProfiles.HasProfile)
                        ?? IAgentChatClientFactory.StandardProfile
                    : request.ModelProfile.Trim();

            if (await ValidateModelProfileAsync(modelProfile, cancellationToken) is { } profileError)
            {
                return ServiceResult<AgentDto>.Fail(profileError, 400);
            }

            var (allowedRoles, rolesError) = NormalizeRoles(request.AllowedRoles);

            if (rolesError != null)
            {
                return ServiceResult<AgentDto>.Fail(rolesError, 400);
            }

            // Tenant-built: not managed by a template.
            var agent = new Agent
            {
                TenantId = ResolveTenantId(),
                AgentId = normalizedAgentId,
                AllowedRoles = allowedRoles,
                Name = request.Name.Trim(),
                Description = request.Description?.Trim(),
                Version = request.Version.Trim(),
                IsActive = request.IsActive,
                Instructions = request.Instructions?.Trim() ?? "",
                MaximumIterationsPerRequest =
                    request.MaximumIterationsPerRequest
                    ?? AgentConfigurationLimits.DefaultIterationsPerRequest,
                AllowMultipleToolCalls = request.AllowMultipleToolCalls ?? false,
                ApprovalLifetimeMinutes =
                    request.ApprovalLifetimeMinutes
                    ?? AgentConfigurationLimits.DefaultApprovalLifetimeMinutes,
                ModelProfile = modelProfile,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            _agents.Add(agent);

            await _agents.SaveChangesAsync(
                cancellationToken);

            return ServiceResult<AgentDto>.Ok(
                await MapAsync(
                    agent,
                    cancellationToken));
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

            // Null keeps the current roles.
            if (request.AllowedRoles is not null)
            {
                var (allowedRoles, rolesError) = NormalizeRoles(request.AllowedRoles);

                if (rolesError != null)
                {
                    return ServiceResult<AgentDto>.Fail(rolesError, 400);
                }

                agent.AllowedRoles = allowedRoles;
            }

            // A managed agent follows its platform template; the tenant only
            // decides whether it is active and who may use it. Other fields are
            // not applied. Detach the agent to edit it freely.
            if (agent.IsManaged)
            {
                agent.IsActive = request.IsActive;
                agent.UpdatedAtUtc = DateTime.UtcNow;

                await _agents.SaveChangesAsync(cancellationToken);

                return ServiceResult<AgentDto>.Ok(
                    await MapAsync(
                        agent,
                        cancellationToken));
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

            // Null keeps the current value (older clients do not send these).
            if (request.Instructions is not null)
            {
                agent.Instructions = request.Instructions.Trim();
            }

            if (request.MaximumIterationsPerRequest is { } iterations)
            {
                agent.MaximumIterationsPerRequest = iterations;
            }

            if (request.AllowMultipleToolCalls is { } allowMultiple)
            {
                agent.AllowMultipleToolCalls = allowMultiple;
            }

            if (request.ApprovalLifetimeMinutes is { } approvalLifetime)
            {
                agent.ApprovalLifetimeMinutes = approvalLifetime;
            }

            if (!string.IsNullOrWhiteSpace(request.ModelProfile))
            {
                var modelProfile = request.ModelProfile.Trim();

                if (await ValidateModelProfileAsync(modelProfile, cancellationToken) is { } profileError)
                {
                    return ServiceResult<AgentDto>.Fail(profileError, 400);
                }

                agent.ModelProfile = modelProfile;
            }

            agent.UpdatedAtUtc =
                DateTime.UtcNow;

            //_agents.Update(agent);

            await _agents.SaveChangesAsync(
                cancellationToken);

            return ServiceResult<AgentDto>.Ok(
                await MapAsync(
                    agent,
                    cancellationToken));
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

            if (agent.IsManaged)
            {
                // Template rollouts would reinstall it; deactivate it instead.
                return ServiceResult<bool>.Fail(
                    "This agent is managed by a platform template. Deactivate it, or detach it first to delete it.",
                    409);
            }

            _agents.Remove(agent);

            await _agents.SaveChangesAsync(
                cancellationToken);

            return ServiceResult<bool>.Ok(true);
        }

        public async Task<ServiceResult<List<AgentToolCatalogItemDto>>> GetToolCatalogAsync(
            CancellationToken cancellationToken = default)
        {
            var authorization =
                await _authorization.AuthorizeAsync(
                    Permissions.AgentsRead,
                    cancellationToken);

            if (!authorization.Allowed)
            {
                return ServiceResult<List<AgentToolCatalogItemDto>>.Fail(
                    authorization.Reason ??
                    "You are not authorized to view the agent tool catalogue.",
                    403);
            }

            // Only the tools the tenant's subscription enables.
            var enabledModules =
                await _entitlements.GetEnabledModulesAsync(cancellationToken);

            var catalogue =
                _toolRegistry
                    .GetCatalogue()
                    .Where(descriptor => enabledModules.Contains(descriptor.Module))
                    .Select(descriptor => new AgentToolCatalogItemDto
                    {
                        Name = descriptor.Name,
                        Description = descriptor.Description,
                        RequiredPermission = descriptor.RequiredPermission,
                        Module = descriptor.Module,
                        ModuleName =
                            PlatformModules.Find(descriptor.Module)?.Name
                            ?? descriptor.Module,
                        RiskLevel = descriptor.RiskLevel.ToString(),
                        RequiresApproval = descriptor.RequiresApproval,
                        MaximumAmount = descriptor.MaximumAmount
                    })
                    .ToList();

            return ServiceResult<List<AgentToolCatalogItemDto>>.Ok(catalogue);
        }

        public async Task<ServiceResult<AgentDto>> UpdateToolsAsync(
            string agentId,
            UpdateAgentToolsRequest request,
            CancellationToken cancellationToken = default)
        {
            // Granting tools changes what an agent can do, so it is governed
            // like permissions rather than like metadata edits.
            var authorization =
                await _authorization.AuthorizeAsync(
                    Permissions.AgentsPermissionsManage,
                    cancellationToken);

            if (!authorization.Allowed)
            {
                return ServiceResult<AgentDto>.Fail(
                    authorization.Reason ??
                    "You are not authorized to manage agent tools.",
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

            if (agent.IsManaged)
            {
                return ServiceResult<AgentDto>.Fail(
                    "This agent's tools come from its platform template. Detach it to change them.",
                    409);
            }

            var normalized = new List<AgentToolBindingEntity>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var tool in request.Tools ?? [])
            {
                var toolName = tool.ToolName?.Trim() ?? "";

                var descriptor = _toolRegistry.FindDescriptor(toolName);

                if (descriptor is null)
                {
                    return ServiceResult<AgentDto>.Fail(
                        $"Tool '{toolName}' is not in the tool catalogue.",
                        400);
                }

                if (!await _entitlements.IsModuleEnabledAsync(
                        descriptor.Module,
                        cancellationToken))
                {
                    return ServiceResult<AgentDto>.Fail(
                        $"Tool '{toolName}' is not included in this workspace's subscription.",
                        400);
                }

                if (!seen.Add(toolName))
                {
                    return ServiceResult<AgentDto>.Fail(
                        $"Tool '{toolName}' is listed more than once.",
                        400);
                }

                AgentRiskLevel? riskLevel = null;

                if (!string.IsNullOrWhiteSpace(tool.RiskLevel))
                {
                    if (!Enum.TryParse<AgentRiskLevel>(
                            tool.RiskLevel,
                            ignoreCase: true,
                            out var parsedRisk) ||
                        !Enum.IsDefined(parsedRisk))
                    {
                        return ServiceResult<AgentDto>.Fail(
                            $"Risk level '{tool.RiskLevel}' is not valid for tool '{toolName}'.",
                            400);
                    }

                    riskLevel = parsedRisk;
                }

                if (tool.MaximumAmount is <= 0)
                {
                    return ServiceResult<AgentDto>.Fail(
                        $"The maximum amount for tool '{toolName}' must be greater than zero.",
                        400);
                }

                // Store only overrides that tighten the catalogue default; an
                // override that loosens it would be ignored at runtime anyway.
                normalized.Add(new AgentToolBindingEntity
                {
                    AgentId = agent.Id,
                    ToolName = toolName,
                    RequiresApproval =
                        tool.RequiresApproval == true && !descriptor.RequiresApproval
                            ? true
                            : null,
                    RiskLevel =
                        riskLevel is { } risk && risk > descriptor.RiskLevel
                            ? risk.ToString()
                            : null,
                    MaximumAmount =
                        tool.MaximumAmount is { } amount &&
                        (descriptor.MaximumAmount is null || amount < descriptor.MaximumAmount)
                            ? amount
                            : null
                });
            }

            var existing =
                await _toolBindings.ListAsync(
                    x => x.AgentId == agent.Id,
                    cancellationToken);

            foreach (var binding in existing)
            {
                _toolBindings.Remove(binding);
            }

            foreach (var binding in normalized)
            {
                _toolBindings.Add(binding);
            }

            agent.UpdatedAtUtc = DateTime.UtcNow;

            await _agents.SaveChangesAsync(cancellationToken);

            return ServiceResult<AgentDto>.Ok(
                await MapAsync(
                    agent,
                    cancellationToken));
        }

        public async Task<ServiceResult<List<AgentModelProfileDto>>> GetModelProfilesAsync(
            CancellationToken cancellationToken = default)
        {
            var authorization =
                await _authorization.AuthorizeAsync(
                    Permissions.AgentsRead,
                    cancellationToken);

            if (!authorization.Allowed)
            {
                return ServiceResult<List<AgentModelProfileDto>>.Fail(
                    authorization.Reason ??
                    "You are not authorized to view model profiles.",
                    403);
            }

            var allowed =
                await _entitlements.GetAllowedModelProfilesAsync(cancellationToken);

            return ServiceResult<List<AgentModelProfileDto>>.Ok(
                _modelProfiles
                    .GetProfiles()
                    .Where(profile => allowed.Contains(profile.Name))
                    .Select(profile => new AgentModelProfileDto
                    {
                        Name = profile.Name,
                        Model = profile.Model,
                        MaxOutputTokens = profile.MaxOutputTokens,
                        Description = profile.Description
                    })
                    .ToList());
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

        /// <summary>
        /// Null when the profile exists and the tenant's subscription allows it,
        /// otherwise the reason it cannot be used.
        /// </summary>
        private async Task<string?> ValidateModelProfileAsync(
            string modelProfile,
            CancellationToken cancellationToken)
        {
            if (!_modelProfiles.HasProfile(modelProfile))
                return $"Model profile '{modelProfile}' does not exist.";

            var allowed =
                await _entitlements.GetAllowedModelProfilesAsync(cancellationToken);

            return allowed.Contains(modelProfile)
                ? null
                : $"Model profile '{modelProfile}' is not included in this workspace's subscription.";
        }

        public async Task<ServiceResult<AgentDto>> DetachAsync(
            string agentId,
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

            if (!agent.IsManaged)
            {
                return ServiceResult<AgentDto>.Fail(
                    "This agent is not managed by a template.",
                    409);
            }

            // From now on this is the tenant's own copy: editable, and skipped
            // by template rollouts.
            agent.TemplateId = null;
            agent.TemplateVersion = null;
            agent.UpdatedAtUtc = DateTime.UtcNow;

            await _agents.SaveChangesAsync(cancellationToken);

            return ServiceResult<AgentDto>.Ok(
                await MapAsync(
                    agent,
                    cancellationToken));
        }

        private static (List<string> Roles, string? Error) NormalizeRoles(
            IEnumerable<string>? roles)
        {
            var normalized = new List<string>();

            foreach (var role in roles ?? [])
            {
                if (string.IsNullOrWhiteSpace(role))
                    continue;

                var canonical = TenantRoles.Normalize(role);

                if (canonical is null)
                {
                    return ([], $"Role must be one of: {string.Join(", ", TenantRoles.All)}.");
                }

                if (!normalized.Contains(canonical))
                {
                    normalized.Add(canonical);
                }
            }

            return (normalized, null);
        }

        private int ResolveTenantId()
        {
            return _executionContextAccessor.Current?.TenantId ??
            throw new InvalidOperationException(
                "Agent creation requires an established tenant execution context.");
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
