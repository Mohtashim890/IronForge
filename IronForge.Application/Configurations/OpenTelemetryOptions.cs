using System.ComponentModel.DataAnnotations;

namespace IronForge.Application.Configurations
{
    public class OpenTelemetryOptions
    {
        public const string SectionName = "OpenTelemetry:Otlp";

        [Required]
        [Url]
        public string EndPoint { get; set; } = string.Empty;

        [Required]
        public string Protocol { get; set; } = string.Empty;

        [Required]
        public string Headers { get; set; } = string.Empty;

        [Required]
        public int TimeoutMilliseconds { get; set; } = 120;

        [Required]
        public string TracesUrlSuffix { get; set; } = "/v1/traces";

        [Required]
        public string MetricsUrlSuffix { get; set; } = "/v1/metrics";

        [Required]
        public string LogsUrlSuffix { get; set; } = "/v1/logs";

    }
}
