using System.ComponentModel.DataAnnotations;

namespace IronForge.Application.Configurations
{
    public class JwtSettings
    {
        public const string SectionName = "JwtSettings";

        [Required]
        public string Key { get; set; } = string.Empty;

        [Required]
        public string Issuer { get; set; } = string.Empty;

        [Required]
        public string Audience { get; set; } = string.Empty;

        [Range(1, int.MaxValue)]
        public int ExpirationMinutes { get; set; }
    }
}
