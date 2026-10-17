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

        /// <summary>
        /// MCP credential lifetime. Credentials never outlive their delegation
        /// (at most one hour), so a longer value would have no effect.
        /// </summary>
        [Range(1, 60)]
        public int ExpirationMinutes { get; set; } = 15;
    }
}
