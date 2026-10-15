using IronForge.Application.Configurations;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.Persistence;
using IronForge.Shared.Models.Agents.Manage;
using Microsoft.Extensions.Options;

namespace IronForge.Application.Agents.Usage;

public interface IAgentQuotaService
{
    /// <summary>Usage and limit of the current tenant for the current month.</summary>
    Task<AgentUsageSummaryDto> GetSummaryAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Throws <see cref="AgentQuotaExceededException"/> when the limit is reached.</summary>
    Task EnsureWithinQuotaAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Monthly (calendar month, UTC) token quota per tenant.
///
/// The limit is the tenant's MonthlyAgentTokenLimit, or the platform default
/// from <see cref="AgentQuotaOptions"/>. It is a soft limit: it is checked
/// before a run starts, so the run that crosses it is allowed to finish.
/// </summary>
public sealed class AgentQuotaService : IAgentQuotaService
{
    private readonly IAgentUsageStore _usage;
    private readonly IRepository<Tenant> _tenants;
    private readonly IExecutionContextAccessor _executionContext;
    private readonly AgentQuotaOptions _options;

    public AgentQuotaService(
        IAgentUsageStore usage,
        IRepository<Tenant> tenants,
        IExecutionContextAccessor executionContext,
        IOptions<AgentQuotaOptions> options)
    {
        _usage = usage;
        _tenants = tenants;
        _executionContext = executionContext;
        _options = options.Value;
    }

    public async Task<AgentUsageSummaryDto> GetSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        var (periodStart, periodEnd) = CurrentPeriod();

        var limit = await GetLimitAsync(cancellationToken);

        var totals =
            await _usage.GetTotalsByAgentAsync(
                periodStart,
                periodEnd,
                cancellationToken);

        var used = totals.Sum(x => x.TotalTokens);

        return new AgentUsageSummaryDto
        {
            PeriodStartUtc = periodStart,
            PeriodEndUtc = periodEnd,
            MonthlyTokenLimit = limit,
            TokensUsed = used,
            TokensRemaining = limit is { } l ? Math.Max(0, l - used) : null,
            IsExceeded = limit is { } max && used >= max,
            Agents = totals
                .OrderByDescending(x => x.TotalTokens)
                .Select(x => new AgentUsageItemDto
                {
                    AgentId = x.AgentId,
                    Calls = x.Calls,
                    InputTokens = x.InputTokens,
                    OutputTokens = x.OutputTokens,
                    TotalTokens = x.TotalTokens
                })
                .ToList()
        };
    }

    public async Task EnsureWithinQuotaAsync(
        CancellationToken cancellationToken = default)
    {
        var limit = await GetLimitAsync(cancellationToken);

        if (limit is null)
        {
            return;
        }

        var (periodStart, periodEnd) = CurrentPeriod();

        var used =
            await _usage.GetTotalTokensAsync(
                periodStart,
                periodEnd,
                cancellationToken);

        if (used >= limit.Value)
        {
            throw new AgentQuotaExceededException(
                limit.Value,
                used,
                periodEnd);
        }
    }

    private async Task<long?> GetLimitAsync(
        CancellationToken cancellationToken)
    {
        var tenantId =
            _executionContext.Current?.TenantId
            ?? throw new InvalidOperationException(
                "A tenant is required to evaluate agent quotas.");

        var tenant =
            await _tenants.FirstOrDefaultAsync(
                x => x.Id == tenantId,
                cancellationToken);

        return tenant?.MonthlyAgentTokenLimit
            ?? _options.DefaultMonthlyTokenLimit;
    }

    private static (DateTime Start, DateTime End) CurrentPeriod()
    {
        var now = DateTime.UtcNow;
        var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return (start, start.AddMonths(1));
    }
}
