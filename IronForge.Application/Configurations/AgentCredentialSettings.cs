using System.ComponentModel.DataAnnotations;

namespace IronForge.Application.Configurations
{
    public class AgentCredentialSettings
    {
        public const string SectionName =
         "AgentCredentialSettings";

        [Required]
        public string Issuer { get; set; } = string.Empty;

        [Required]
        public string Audience { get; set; } = string.Empty;

        [Range(1, int.MaxValue)]
        public int ExpirationMinutes { get; set; } = 15;
    }
}
