using System.ComponentModel.DataAnnotations;

namespace IronForge.Application.Configurations;

/// <summary>Public URLs used in emails and payment redirects.</summary>
public sealed class AppUrlOptions
{
    public const string SectionName = "AppUrls";

    /// <summary>Page where users choose a new password (web app).</summary>
    [Required]
    [Url]
    public string ResetPasswordUrl { get; set; } = "https://localhost:7180/reset-password";

    /// <summary>Gym admin web app (links in emails).</summary>
    [Required]
    [Url]
    public string WebBaseUrl { get; set; } = "https://localhost:7180";

    /// <summary>Public base URL of this API, for payment pages and redirects.</summary>
    [Required]
    [Url]
    public string ApiBaseUrl { get; set; } = "https://localhost:7226";
}
