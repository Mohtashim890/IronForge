using IronForge.Application.Execution;

namespace IronForge.Application.Observablity.Tracing
{
    public sealed class TelemetryContext
    {
        private readonly IExecutionContextAccessor _executionContext;

        public TelemetryContext(
            IExecutionContextAccessor executionContext)
        {
            _executionContext = executionContext;
        }

        public string? CorrelationId =>
            _executionContext.Current?.CorrelationId;

        public string? TraceId =>
            _executionContext.Current?.TraceId;

        public string? SpanId =>
            _executionContext.Current?.SpanId;

        public int? TenantId =>
            _executionContext.Current?.TenantId;

        public int? UserId =>
            _executionContext.Current?.Actor.UserId;

        public string? AgentId =>
            _executionContext.Current?.Actor.Agent?.AgentId;

        public string? ClientId =>
            _executionContext.Current?.Actor.ClientId;
    }
}
