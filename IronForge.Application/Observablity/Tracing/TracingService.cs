using IronForge.Application.Execution;
using IronForge.Application.Observablity.Tracing;
using System.Diagnostics;

namespace IronForge.Application.Observablity.Tracing
{
    public sealed class TracingService : ITracingService
    {
        private readonly IExecutionContextAccessor _executionContext;

        public TracingService(
            IExecutionContextAccessor executionContext)
        {
            _executionContext = executionContext;
        }

        public Activity? Start(
            string name,
            ActivityKind kind = ActivityKind.Internal)
        {
            var activity =
                IronForgeActivitySource.Source.StartActivity(
                    name,
                    kind);

            if (activity == null)
            {
                return null;
            }

            activity.SetExecutionContext(
                _executionContext.Current);

            return activity;
        }
    }
}
