namespace IronForge.Application.Agents.Usage;

/// <summary>
/// The tenant has used its monthly agent token allowance.
/// The API maps this to HTTP 429.
/// </summary>
public sealed class AgentQuotaExceededException : Exception
{
    public AgentQuotaExceededException(
        long limit,
        long used,
        DateTime resetsAtUtc)
        : base(
            $"Your organisation has used its monthly agent allowance " +
            $"({used:N0} of {limit:N0} tokens). It resets on {resetsAtUtc:yyyy-MM-dd} (UTC).")
    {
        Limit = limit;
        Used = used;
        ResetsAtUtc = resetsAtUtc;
    }

    public long Limit { get; }

    public long Used { get; }

    public DateTime ResetsAtUtc { get; }
}
