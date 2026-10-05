using System.Diagnostics.Metrics;

namespace IronForge.Application.Observablity.Metrics
{
    public static class IronForgeMeter
    {
        public const string Name = "IronForge.Api";
        public const string Version = "1.0.0";

        public static readonly Meter Meter =
            new(Name, Version);
    }
}
