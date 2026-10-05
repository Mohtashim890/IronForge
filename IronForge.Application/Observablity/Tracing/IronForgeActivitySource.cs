using System.Diagnostics;

namespace IronForge.Application.Observablity.Tracing
{
    public static class IronForgeActivitySource
    {
        public const string Name =
            "IronForge.Api";

        public const string Version =
            "1.0.0";

        public static readonly ActivitySource Source =
            new(
                Name,
                Version);
    }
}
