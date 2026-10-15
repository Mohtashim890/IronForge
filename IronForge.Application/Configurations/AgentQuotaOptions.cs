using System.ComponentModel.DataAnnotations;

namespace IronForge.Application.Configurations;

public sealed class AgentQuotaOptions
{
    public const string SectionName = "AgentQuotas";

    /// <summary>
    /// Monthly token limit per tenant for internal agents (calendar month,
    /// UTC). Null means unlimited. A tenant's MonthlyAgentTokenLimit column
    /// overrides this value.
    /// </summary>
    [Range(1, long.MaxValue)]
    public long? DefaultMonthlyTokenLimit { get; set; }
}
