using IronForge.Application.Agents.ModelProfiles;
using IronForge.Application.Agents.Runtime;
using IronForge.Application.Auditing.Models;
using IronForge.Application.Auth.Services;
using IronForge.Application.Commons;
using IronForge.Application.Configurations;
using IronForge.Application.Entities;
using IronForge.Application.Tenants.Services;
using IronForge.Infrastructure.Data;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Gym;
using IronForge.Shared.Models.Tenants;
using IronForge.Shared.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IronForge.Infrastructure.Persistence;

/// <summary>See <see cref="IPlatformInsightsService"/>.</summary>
public sealed class PlatformInsightsService : IPlatformInsightsService
{
    private readonly IronForgeDbContext _db;
    private readonly IUserAuthorizationService _authorization;
    private readonly IAgentChatClientFactory _modelProfiles;
    private readonly AgentQuotaOptions _quota;
    private readonly TimeProvider _clock;

    public PlatformInsightsService(
        IronForgeDbContext db,
        IUserAuthorizationService authorization,
        IAgentChatClientFactory modelProfiles,
        IOptions<AgentQuotaOptions> quota,
        TimeProvider clock)
    {
        _db = db;
        _authorization = authorization;
        _modelProfiles = modelProfiles;
        _quota = quota.Value;
        _clock = clock;
    }

    public async Task<ServiceResult<PlatformOverviewDto>> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        if (!IsOperator())
            return Forbidden<PlatformOverviewDto>();

        var now = _clock.GetUtcNow().UtcDateTime;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var since = now.Date.AddDays(-29);

        var tenants = await _db.Tenants.AsNoTracking().ToListAsync(cancellationToken);
        var memberCounts = await ActiveMembershipCountsAsync(cancellationToken);
        var dtos = tenants.Select(x => Map(x, memberCounts.GetValueOrDefault(x.Id), now)).ToList();

        var usage = await _db.AgentUsageRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CreatedAtUtc >= since)
            .GroupBy(x => x.CreatedAtUtc.Date)
            .Select(g => new { Day = g.Key, Tokens = g.Sum(x => x.TotalTokens) })
            .ToListAsync(cancellationToken);

        var byDay = usage.ToDictionary(x => DateOnly.FromDateTime(x.Day), x => x.Tokens);

        var overview = new PlatformOverviewDto
        {
            TenantCount = tenants.Count,
            ActiveTenants = tenants.Count(x => x.IsActive && x.SubscriptionStatus == SubscriptionStatus.Active),
            TrialTenants = tenants.Count(x => x.IsActive && x.SubscriptionStatus == SubscriptionStatus.Trial),
            PastDueTenants = tenants.Count(x => x.IsActive && x.SubscriptionStatus == SubscriptionStatus.PastDue),
            CanceledTenants = tenants.Count(x => x.SubscriptionStatus == SubscriptionStatus.Canceled),
            SuspendedTenants = tenants.Count(x => !x.IsActive),
            ActiveGymMembers = await _db.MemberProfiles
                .IgnoreQueryFilters()
                .CountAsync(x => x.Status == MemberStatus.Active, cancellationToken),
            StaffAccounts = await _db.TenantMemberships
                .CountAsync(x => x.IsActive && x.Role != TenantRoles.Member, cancellationToken),
            TokensThisMonth = await _db.AgentUsageRecords
                .IgnoreQueryFilters()
                .Where(x => x.CreatedAtUtc >= monthStart)
                .SumAsync(x => (long?)x.TotalTokens, cancellationToken) ?? 0,
            TokensByDay = Enumerable.Range(0, 30)
                .Select(i => DateOnly.FromDateTime(since.AddDays(i)))
                .Select(day => new DailyTokensDto { Day = day, Tokens = byDay.GetValueOrDefault(day) })
                .ToList(),
            TenantsByPlan = tenants
                .GroupBy(x => x.Plan)
                .Select(g => new PlanCountDto { Plan = g.Key, Tenants = g.Count() })
                .OrderByDescending(x => x.Tenants)
                .ToList(),
            TrialsEndingSoon = dtos
                .Where(x => x.SubscriptionStatus == SubscriptionStatus.Trial &&
                            x.TrialEndsAtUtc is { } end && end <= now.AddDays(7))
                .OrderBy(x => x.TrialEndsAtUtc)
                .ToList(),
            PastDue = dtos.Where(x => x.SubscriptionStatus == SubscriptionStatus.PastDue).ToList()
        };

        return ServiceResult<PlatformOverviewDto>.Ok(overview);
    }

    public async Task<ServiceResult<PlatformTenantDetailDto>> GetTenantAsync(int tenantId, CancellationToken cancellationToken = default)
    {
        if (!IsOperator())
            return Forbidden<PlatformTenantDetailDto>();

        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId, cancellationToken);

        if (tenant is null)
            return ServiceResult<PlatformTenantDetailDto>.Fail("Tenant was not found.", 404);

        var now = _clock.GetUtcNow().UtcDateTime;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var since = now.Date.AddDays(-29);

        var settings = await _db.GymSettings.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);

        var memberCount = await _db.TenantMemberships.CountAsync(x => x.TenantId == tenantId && x.IsActive, cancellationToken);

        var byStatus = await _db.MemberProfiles
            .IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.AnonymizedAtUtc == null)
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var usage = await _db.AgentUsageRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.CreatedAtUtc >= since)
            .GroupBy(x => x.CreatedAtUtc.Date)
            .Select(g => new { Day = g.Key, Tokens = g.Sum(x => x.TotalTokens) })
            .ToListAsync(cancellationToken);

        var usageByDay = usage.ToDictionary(x => DateOnly.FromDateTime(x.Day), x => x.Tokens);

        var templates = AgentTemplates.Default.ToDictionary(x => x.TemplateId, x => x.Version);

        var detail = new PlatformTenantDetailDto
        {
            Tenant = Map(tenant, memberCount, now),
            Currency = settings?.Currency,
            TimeZoneId = settings?.TimeZoneId,
            JoinCode = settings?.JoinCode,
            MembersByStatus = byStatus.ToDictionary(x => x.Status.ToString(), x => x.Count),
            StaffCount = await _db.TenantMemberships.CountAsync(
                x => x.TenantId == tenantId && x.IsActive && x.Role != TenantRoles.Member,
                cancellationToken),
            GymRevenueLast30Days = await _db.Payments
                .IgnoreQueryFilters()
                .Where(x => x.TenantId == tenantId && x.PaidAtUtc >= since)
                .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0,
            TokensThisMonth = await _db.AgentUsageRecords
                .IgnoreQueryFilters()
                .Where(x => x.TenantId == tenantId && x.CreatedAtUtc >= monthStart)
                .SumAsync(x => (long?)x.TotalTokens, cancellationToken) ?? 0,
            EffectiveMonthlyTokenLimit =
                tenant.MonthlyAgentTokenLimit
                ?? SubscriptionPlans.Find(tenant.Plan)?.MonthlyAgentTokenLimit
                ?? _quota.DefaultMonthlyTokenLimit,
            TokensByDay = Enumerable.Range(0, 30)
                .Select(i => DateOnly.FromDateTime(since.AddDays(i)))
                .Select(day => new DailyTokensDto { Day = day, Tokens = usageByDay.GetValueOrDefault(day) })
                .ToList(),
            Agents = (await _db.Agents
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(x => x.TenantId == tenantId)
                    .OrderBy(x => x.Name)
                    .ToListAsync(cancellationToken))
                .Select(x => new PlatformTenantAgentDto
                {
                    AgentId = x.AgentId,
                    Name = x.Name,
                    IsActive = x.IsActive,
                    IsManaged = x.TemplateId != null,
                    TemplateVersion = x.TemplateVersion,
                    LatestTemplateVersion = x.TemplateId is { } id && templates.TryGetValue(id, out var version) ? version : null,
                    ModelProfile = x.ModelProfile
                })
                .ToList(),
            RecentActivity = await AuditQuery(tenantId, null, null, now.AddDays(-30))
                .Take(15)
                .ToListAsync(cancellationToken)
        };

        return ServiceResult<PlatformTenantDetailDto>.Ok(detail);
    }

    public async Task<ServiceResult<PlatformCatalogDto>> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        if (!IsOperator())
            return Forbidden<PlatformCatalogDto>();

        var tenantsByPlan = await _db.Tenants
            .GroupBy(x => x.Plan)
            .Select(g => new { Plan = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Plan, x => x.Count, cancellationToken);

        var agentVersions = await _db.Agents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.TemplateId != null)
            .Select(x => new { x.TemplateId, x.TemplateVersion })
            .ToListAsync(cancellationToken);

        var catalog = new PlatformCatalogDto
        {
            Plans = SubscriptionPlans.All
                .Select(plan => new PlatformPlanDto
                {
                    Id = plan.Id,
                    Name = plan.Name,
                    IsDefault = plan.Id == SubscriptionPlans.Default.Id,
                    Modules = plan.Modules.ToList(),
                    MonthlyAgentTokenLimit = plan.MonthlyAgentTokenLimit,
                    ModelProfiles = plan.ModelProfiles.ToList(),
                    TenantCount = tenantsByPlan.GetValueOrDefault(plan.Id)
                })
                .ToList(),
            Modules = PlatformModules.All
                .Select(x => new PlatformModuleDto { Id = x.Id, Name = x.Name, Description = x.Description, IsCustom = x.IsCustom })
                .ToList(),
            ModelProfiles = _modelProfiles.GetProfiles().Select(x => x.Name).ToList(),
            AgentTemplates = AgentTemplates.Default
                .Select(template => new PlatformTemplateDto
                {
                    TemplateId = template.TemplateId,
                    Name = template.Name,
                    Description = template.Description,
                    Version = template.Version,
                    Modules = template.Modules.ToList(),
                    Tools = template.Definition.Tools.Select(x => x.ToolName).ToList(),
                    AllowedRoles = template.Definition.AllowedRoles.ToList(),
                    InstalledTenants = agentVersions.Count(x => x.TemplateId == template.TemplateId),
                    OutdatedTenants = agentVersions.Count(x => x.TemplateId == template.TemplateId && x.TemplateVersion < template.Version)
                })
                .ToList()
        };

        return ServiceResult<PlatformCatalogDto>.Ok(catalog);
    }

    public async Task<ServiceResult<List<PlatformAuditEventDto>>> GetAuditAsync(
        PlatformAuditQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!IsOperator())
            return Forbidden<List<PlatformAuditEventDto>>();

        AuditCategory? category = Enum.TryParse<AuditCategory>(query.Category, ignoreCase: true, out var c) ? c : null;
        AuditOutcome? outcome = Enum.TryParse<AuditOutcome>(query.Outcome, ignoreCase: true, out var o) ? o : null;

        var from = _clock.GetUtcNow().UtcDateTime.AddDays(-Math.Clamp(query.Days, 1, 90));

        var items = await AuditQuery(query.TenantId, category, outcome, from)
            .Take(Math.Clamp(query.Take, 1, 500))
            .ToListAsync(cancellationToken);

        return ServiceResult<List<PlatformAuditEventDto>>.Ok(items);
    }

    private IQueryable<PlatformAuditEventDto> AuditQuery(
        int? tenantId,
        AuditCategory? category,
        AuditOutcome? outcome,
        DateTime from)
    {
        var events = _db.AuditEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.OccurredAtUtc >= from);

        if (tenantId.HasValue)
            events = events.Where(x => x.TenantId == tenantId);
        if (category.HasValue)
            events = events.Where(x => x.Category == category);
        if (outcome.HasValue)
            events = events.Where(x => x.Outcome == outcome);

        return events
            .OrderByDescending(x => x.OccurredAtUtc)
            .GroupJoin(
                _db.Tenants,
                e => e.TenantId,
                t => t.Id,
                (e, t) => new { Event = e, Tenants = t })
            .SelectMany(
                x => x.Tenants.DefaultIfEmpty(),
                (x, tenant) => new PlatformAuditEventDto
                {
                    OccurredAtUtc = x.Event.OccurredAtUtc,
                    TenantId = x.Event.TenantId,
                    TenantName = tenant != null ? tenant.Name : null,
                    Category = x.Event.Category.ToString(),
                    Action = x.Event.Action,
                    Outcome = x.Event.Outcome.ToString(),
                    UserId = x.Event.UserId,
                    AgentId = x.Event.AgentId,
                    ResourceType = x.Event.ResourceType,
                    ResourceId = x.Event.ResourceId,
                    Reason = x.Event.Reason
                });
    }

    private async Task<Dictionary<int, int>> ActiveMembershipCountsAsync(CancellationToken cancellationToken) =>
        await _db.TenantMemberships
            .Where(x => x.IsActive)
            .GroupBy(x => x.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, cancellationToken);

    private bool IsOperator() => _authorization.HasPermission(Permissions.PlatformTenantsManage);

    private static ServiceResult<T> Forbidden<T>() =>
        ServiceResult<T>.Fail("Only platform administrators can view this.", 403);

    private static PlatformTenantDto Map(Tenant tenant, int activeMembers, DateTime now) => new()
    {
        TenantId = tenant.Id,
        Name = tenant.Name,
        Slug = tenant.Slug,
        IsActive = tenant.IsActive,
        CreatedAtUtc = tenant.CreatedAtUtc,
        ActiveMemberCount = activeMembers,
        MonthlyAgentTokenLimit = tenant.MonthlyAgentTokenLimit,
        Plan = tenant.Plan,
        SubscriptionStatus = tenant.SubscriptionStatus,
        TrialEndsAtUtc = tenant.TrialEndsAtUtc,
        AddOnModules = tenant.AddOnModules.ToList(),
        EnabledModules = tenant.GetEnabledModules().Order().ToList(),
        AddOnModelProfiles = tenant.AddOnModelProfiles.ToList(),
        AllowedModelProfiles = tenant.GetAllowedModelProfiles().ToList(),
        HasAccess = tenant.HasAccess(now)
    };
}
