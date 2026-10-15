using IronForge.Application.Entities;

namespace IronForge.Application.Agents.Usage;

/// <summary>
/// Persistence for agent token usage. A dedicated store (rather than the
/// generic repository) because quotas need server-side aggregation
/// (SUM / GROUP BY), which IRepository deliberately does not expose.
/// All queries are tenant-filtered.
/// </summary>
public interface IAgentUsageStore
{
    Task AddAsync(
        AgentUsageRecord record,
        CancellationToken cancellationToken = default);

    Task<long> GetTotalTokensAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentUsageTotal>> GetTotalsByAgentAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);
}

public sealed record AgentUsageTotal(
    string AgentId,
    int Calls,
    long InputTokens,
    long OutputTokens,
    long TotalTokens);
