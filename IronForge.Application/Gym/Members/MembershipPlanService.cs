using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Commons;
using IronForge.Application.Entities;
using IronForge.Application.Gym.Common;
using IronForge.Application.Persistence;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Gym;

namespace IronForge.Application.Gym.Members;

public interface IMembershipPlanService
{
    Task<ServiceResult<List<MembershipPlanDto>>> ListAsync(
        bool includeArchived,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<MembershipPlanDto>> CreateAsync(
        SaveMembershipPlanRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Price changes apply from each member's next invoice; issued invoices
    /// keep their amount.
    /// </summary>
    Task<ServiceResult<MembershipPlanDto>> UpdateAsync(
        int planId,
        SaveMembershipPlanRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class MembershipPlanService : IMembershipPlanService
{
    private readonly IGymAccessService _access;
    private readonly IRepository<MembershipPlan> _plans;
    private readonly IRepository<MemberProfile> _members;
    private readonly IRepository<GymSettings> _settings;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;

    public MembershipPlanService(
        IGymAccessService access,
        IRepository<MembershipPlan> plans,
        IRepository<MemberProfile> members,
        IRepository<GymSettings> settings,
        IAuditService audit,
        TimeProvider clock)
    {
        _access = access;
        _plans = plans;
        _members = members;
        _settings = settings;
        _audit = audit;
        _clock = clock;
    }

    public async Task<ServiceResult<List<MembershipPlanDto>>> ListAsync(
        bool includeArchived,
        CancellationToken cancellationToken = default)
    {
        // Staff and members both see the plans (members choose one when joining).
        var staff = await _access.AuthorizeAsync(Permissions.GymMembersRead, cancellationToken);
        var authorization = staff.Allowed
            ? staff
            : await _access.AuthorizeAsync(Permissions.GymSelfRead, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<List<MembershipPlanDto>>(authorization);
        }

        var tenantId = _access.TenantId!.Value;
        includeArchived &= staff.Allowed;

        var plans = await _plans.QueryAsync(
            q => q
                .Where(x => includeArchived || x.IsActive)
                .OrderBy(x => x.Price),
            cancellationToken);

        var counts = staff.Allowed
            ? (await _members.QueryAsync(
                    q => q
                        .Where(x => x.Status == MemberStatus.Active && x.MembershipPlanId != null)
                        .GroupBy(x => x.MembershipPlanId!.Value)
                        .Select(g => new { PlanId = g.Key, Count = g.Count() }),
                    cancellationToken))
                .ToDictionary(x => x.PlanId, x => x.Count)
            : [];

        var currency = await GetCurrencyAsync(tenantId, cancellationToken);

        return ServiceResult<List<MembershipPlanDto>>.Ok(
            plans.Select(x => Map(x, currency, counts.GetValueOrDefault(x.Id))).ToList());
    }

    public async Task<ServiceResult<MembershipPlanDto>> CreateAsync(
        SaveMembershipPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymPlansManage, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<MembershipPlanDto>(authorization);
        }

        var tenantId = _access.TenantId!.Value;
        var name = request.Name.Trim();

        if (await _plans.AnyAsync(x => x.Name.ToLower() == name.ToLower(), cancellationToken))
        {
            return GymResults.Conflict<MembershipPlanDto>($"A plan named '{name}' already exists.");
        }

        var plan = new MembershipPlan
        {
            TenantId = tenantId,
            Name = name,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Price = decimal.Round(request.Price, 2),
            BillingInterval = request.BillingInterval,
            IsActive = request.IsActive,
            CreatedAtUtc = _clock.GetUtcNow().UtcDateTime
        };

        _plans.Add(plan);

        await _plans.SaveChangesAsync(cancellationToken);

        await _audit.RecordAsync(
            AuditCategory.Membership,
            "MembershipPlanCreated",
            AuditOutcome.Succeeded,
            resourceType: "MembershipPlan",
            resourceId: plan.Id.ToString(),
            reason: $"{plan.Name}: {plan.Price} {plan.BillingInterval}.",
            cancellationToken: cancellationToken);

        return ServiceResult<MembershipPlanDto>.Ok(
            Map(plan, await GetCurrencyAsync(tenantId, cancellationToken), 0));
    }

    public async Task<ServiceResult<MembershipPlanDto>> UpdateAsync(
        int planId,
        SaveMembershipPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymPlansManage, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<MembershipPlanDto>(authorization);
        }

        var plan = await _plans.FirstOrDefaultTrackedAsync(x => x.Id == planId, cancellationToken);

        if (plan is null)
        {
            return GymResults.NotFound<MembershipPlanDto>("Membership plan");
        }

        var name = request.Name.Trim();

        if (await _plans.AnyAsync(x => x.Id != planId && x.Name.ToLower() == name.ToLower(), cancellationToken))
        {
            return GymResults.Conflict<MembershipPlanDto>($"A plan named '{name}' already exists.");
        }

        var previous = $"{plan.Name}: {plan.Price} {plan.BillingInterval}, active={plan.IsActive}";

        plan.Name = name;
        plan.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        plan.Price = decimal.Round(request.Price, 2);
        plan.BillingInterval = request.BillingInterval;
        plan.IsActive = request.IsActive;

        await _plans.SaveChangesAsync(cancellationToken);

        await _audit.RecordAsync(
            AuditCategory.Membership,
            "MembershipPlanUpdated",
            AuditOutcome.Succeeded,
            resourceType: "MembershipPlan",
            resourceId: plan.Id.ToString(),
            reason: $"{previous} -> {plan.Name}: {plan.Price} {plan.BillingInterval}, active={plan.IsActive}.",
            cancellationToken: cancellationToken);

        var members = await _members.CountAsync(
            x => x.MembershipPlanId == plan.Id && x.Status == MemberStatus.Active,
            cancellationToken);

        return ServiceResult<MembershipPlanDto>.Ok(
            Map(plan, await GetCurrencyAsync(plan.TenantId, cancellationToken), members));
    }

    private async Task<string> GetCurrencyAsync(int tenantId, CancellationToken cancellationToken) =>
        (await _settings.FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken))?.Currency ?? "USD";

    public static MembershipPlanDto Map(MembershipPlan plan, string currency, int activeMembers) => new()
    {
        Id = plan.Id,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        Currency = currency,
        BillingInterval = plan.BillingInterval,
        IsActive = plan.IsActive,
        ActiveMemberCount = activeMembers
    };
}
