using IronForge.Application.Agents.Runtime;
using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Auth.Services;
using IronForge.Application.Commons;
using IronForge.Application.Entities;
using IronForge.Application.Persistence;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Tenants;

namespace IronForge.Application.Tenants.Services;

/// <summary>
/// Brings every tenant's template agents up to date. For each tenant and
/// each <see cref="AgentTemplates.Default"/> template its plan covers:
/// - missing: installed as a managed agent (e.g. after an upgrade);
/// - managed with an older version: updated (active state and allowed roles
///   are kept, tools replaced);
/// - detached (the tenant's own copy): left alone.
/// Safe to run repeatedly: an up-to-date tenant is not changed.
/// </summary>
public interface IAgentTemplateRolloutService
{
    Task<ServiceResult<TemplateRolloutResultDto>> RolloutAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The same for one tenant. Runs after its subscription changes, so an
    /// upgrade or add-on (e.g. coaching) installs the agents it now covers.
    /// </summary>
    Task<ServiceResult<TemplateRolloutResultDto>> RolloutToTenantAsync(
        int tenantId,
        CancellationToken cancellationToken = default);
}

public sealed class AgentTemplateRolloutService : IAgentTemplateRolloutService
{
    private readonly IRepository<Tenant> _tenants;
    private readonly ITemplateAgentStore _agents;
    private readonly IUserAuthorizationService _authorization;
    private readonly IAuditService _auditService;

    public AgentTemplateRolloutService(
        IRepository<Tenant> tenants,
        ITemplateAgentStore agents,
        IUserAuthorizationService authorization,
        IAuditService auditService)
    {
        _tenants = tenants;
        _agents = agents;
        _authorization = authorization;
        _auditService = auditService;
    }

    public async Task<ServiceResult<TemplateRolloutResultDto>> RolloutAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_authorization.HasPermission(Permissions.PlatformTenantsManage))
        {
            return Forbidden();
        }

        var result = new TemplateRolloutResultDto();

        foreach (var tenant in await _tenants.ListAsync(cancellationToken))
        {
            await RolloutAsync(tenant, result, cancellationToken);
        }

        return ServiceResult<TemplateRolloutResultDto>.Ok(result);
    }

    public async Task<ServiceResult<TemplateRolloutResultDto>> RolloutToTenantAsync(
        int tenantId,
        CancellationToken cancellationToken = default)
    {
        if (!_authorization.HasPermission(Permissions.PlatformTenantsManage))
        {
            return Forbidden();
        }

        var tenant = await _tenants.FirstOrDefaultAsync(x => x.Id == tenantId, cancellationToken);

        if (tenant is null)
        {
            return ServiceResult<TemplateRolloutResultDto>.Fail("Tenant was not found.", 404);
        }

        var result = new TemplateRolloutResultDto();

        await RolloutAsync(tenant, result, cancellationToken);

        return ServiceResult<TemplateRolloutResultDto>.Ok(result);
    }

    private async Task RolloutAsync(
        Tenant tenant,
        TemplateRolloutResultDto result,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var modules = tenant.GetEnabledModules();
        var profiles = tenant.GetAllowedModelProfiles();
        var agents = await _agents.ListAgentsAsync(tenant.Id, cancellationToken);

        var changes = new List<string>();

        foreach (var template in AgentTemplates.Default)
        {
            if (!template.IsCoveredBy(modules))
                continue;

            var existing = agents.FirstOrDefault(x => x.AgentId == template.TemplateId);

            if (existing is null)
            {
                var agent = template.CreateAgent(tenant.Id, now);
                agent.ModelProfile = template.ChooseModelProfile(profiles);

                _agents.Add(agent);

                result.AgentsInstalled++;
                changes.Add($"installed {template.TemplateId} v{template.Version}");
            }
            else if (existing.TemplateId != template.TemplateId)
            {
                // Detached, or a tenant-built agent with the same id.
                result.DetachedSkipped++;
            }
            else if ((existing.TemplateVersion ?? 0) < template.Version)
            {
                var from = existing.TemplateVersion;

                template.ApplyTo(existing, now);
                existing.ModelProfile = template.ChooseModelProfile(profiles);

                result.AgentsUpdated++;
                changes.Add($"updated {template.TemplateId} v{from} -> v{template.Version}");
            }
        }

        if (changes.Count == 0)
            return;

        await _agents.SaveChangesAsync(cancellationToken);

        result.TenantsChanged++;

        await _auditService.RecordAsync(
            AuditCategory.Agent,
            "AgentTemplatesRolledOut",
            AuditOutcome.Succeeded,
            actorType: AuditActorType.User,
            resourceType: "Tenant",
            resourceId: tenant.Id.ToString(),
            reason: string.Join("; ", changes),
            cancellationToken: cancellationToken,
            tenantId: tenant.Id);
    }

    private static ServiceResult<TemplateRolloutResultDto> Forbidden() =>
        ServiceResult<TemplateRolloutResultDto>.Fail(
            "Only platform administrators can roll out agent templates.",
            403);
}
