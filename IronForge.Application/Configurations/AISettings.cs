using System.ComponentModel.DataAnnotations;

namespace IronForge.Application.Configurations
{
    public class AISettings
    {
        public const string SectionName = "AISettings";

        [Required]
        public string ApiKey { get; set; } = string.Empty;

        [Required]
        public string Model { get; set; } = string.Empty;
    }
}
