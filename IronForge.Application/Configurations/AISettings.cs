using System.ComponentModel.DataAnnotations;

namespace IronForge.Application.Configurations
{
    public class AISettings
    {
        public const string SectionName = "AISettings";

        [Required]
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Default model. Used by the "standard" profile and by any profile
        /// that does not set its own model.
        /// </summary>
        [Required]
        public string Model { get; set; } = string.Empty;

        /// <summary>
        /// Named model profiles agents can be assigned to, keyed by profile
        /// name. A "standard" profile always exists, even if not configured.
        /// Example: AISettings__ModelProfiles__economy__Model=&lt;cheaper-model&gt;
        /// </summary>
        public Dictionary<string, AIModelProfileOptions> ModelProfiles { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    public class AIModelProfileOptions
    {
        /// <summary>Model id; empty means <see cref="AISettings.Model"/>.</summary>
        public string? Model { get; set; }

        /// <summary>Upper bound on output tokens per model call.</summary>
        [Range(1, 128_000)]
        public int? MaxOutputTokens { get; set; }

        public string? Description { get; set; }
    }
}
