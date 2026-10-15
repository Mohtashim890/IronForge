namespace IronForge.Shared.Models.Agents.Manage;

/// <summary>The tenant's agent token usage for the current quota period.</summary>
public sealed class AgentUsageSummaryDto
{
    public DateTime PeriodStartUtc { get; set; }

    public DateTime PeriodEndUtc { get; set; }

    /// <summary>Null means unlimited.</summary>
    public long? MonthlyTokenLimit { get; set; }

    public long TokensUsed { get; set; }

    public long? TokensRemaining { get; set; }

    public bool IsExceeded { get; set; }

    public List<AgentUsageItemDto> Agents { get; set; } = [];
}

public sealed class AgentUsageItemDto
{
    public string AgentId { get; set; } = string.Empty;

    public int Calls { get; set; }

    public long InputTokens { get; set; }

    public long OutputTokens { get; set; }

    public long TotalTokens { get; set; }
}
