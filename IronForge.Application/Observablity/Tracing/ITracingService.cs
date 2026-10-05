using System.Diagnostics;

namespace IronForge.Application.Observablity.Tracing
{
    public interface ITracingService
    {
        Activity? Start(
            string name,
            ActivityKind kind = ActivityKind.Internal);
    }
}
