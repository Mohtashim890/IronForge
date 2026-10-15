using IronForge.Application.Agents.Usage;
using IronForge.Application.Entities;
using IronForge.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace IronForge.Infrastructure.Persistence;

/// <summary>
/// EF implementation of <see cref="IAgentUsageStore"/>. Aggregates run in
/// PostgreSQL (SUM / GROUP BY over the (TenantId, CreatedAtUtc) index); the
/// tenant query filter on AgentUsageRecord keeps them tenant-scoped.
/// </summary>
public sealed class AgentUsageStore : IAgentUsageStore
{
    private readonly IronForgeDbContext _dbContext;

    public AgentUsageStore(IronForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(
        AgentUsageRecord record,
        CancellationToken cancellationToken = default)
    {
        // Saved immediately and then detached. The runtime records usage after
        // its conversation writes, which are already saved, so this save does
        // not carry other pending changes.
        _dbContext.AgentUsageRecords.Add(record);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _dbContext.Entry(record).State = EntityState.Detached;
        }
    }

    public async Task<long> GetTotalTokensAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.AgentUsageRecords
            .Where(x => x.CreatedAtUtc >= fromUtc && x.CreatedAtUtc < toUtc)
            .SumAsync(x => x.TotalTokens, cancellationToken);
    }

    public async Task<IReadOnlyList<AgentUsageTotal>> GetTotalsByAgentAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.AgentUsageRecords
            .Where(x => x.CreatedAtUtc >= fromUtc && x.CreatedAtUtc < toUtc)
            .GroupBy(x => x.AgentId)
            .Select(g => new AgentUsageTotal(
                g.Key,
                g.Count(),
                g.Sum(x => x.InputTokens),
                g.Sum(x => x.OutputTokens),
                g.Sum(x => x.TotalTokens)))
            .ToListAsync(cancellationToken);
    }
}
