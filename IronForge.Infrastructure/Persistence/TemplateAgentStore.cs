using IronForge.Application.Agents.Runtime;
using IronForge.Application.Entities;
using IronForge.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace IronForge.Infrastructure.Persistence;

/// <summary>
/// See <see cref="ITemplateAgentStore"/>. The tenant query filter is bypassed
/// on purpose and replaced by an explicit tenant condition.
/// </summary>
public sealed class TemplateAgentStore : ITemplateAgentStore
{
    private readonly IronForgeDbContext _dbContext;

    public TemplateAgentStore(IronForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<List<Agent>> ListAgentsAsync(
        int tenantId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Agents
            .IgnoreQueryFilters()
            .Include(x => x.ToolBindings)
            .Where(x => x.TenantId == tenantId)
            .ToListAsync(cancellationToken);
    }

    public void Add(Agent agent)
    {
        _dbContext.Agents.Add(agent);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}
