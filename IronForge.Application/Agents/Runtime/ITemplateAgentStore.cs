using IronForge.Application.Entities;

namespace IronForge.Application.Agents.Runtime;

/// <summary>
/// Agent persistence for template rollouts. Rollouts run as the platform
/// operator without a tenant context, where the tenant query filter cannot
/// apply, so every method takes the tenant explicitly.
/// </summary>
public interface ITemplateAgentStore
{
    /// <summary>The tenant's agents with their tool bindings, tracked.</summary>
    Task<List<Agent>> ListAgentsAsync(
        int tenantId,
        CancellationToken cancellationToken = default);

    void Add(Agent agent);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
