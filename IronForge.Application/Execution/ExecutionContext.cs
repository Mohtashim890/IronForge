namespace IronForge.Application.Execution;

public class ExecutionContext
{
    public int? TenantId { get; init; }
    public ExecutionActor Actor { get; init; } = new();

    public ExecutionSource Source { get; init; }

    public string? CorrelationId { get; init; }
    public string? TraceId { get; init; }
    public string? SpanId { get; init; }
}