using System.ComponentModel.DataAnnotations;

namespace IronForge.Shared.Models.Tenants
{
    public sealed class TenantInvitationDto
    {
        public Guid InvitationId { get; set; }

        public string Email { get; set; } = "";

        public string Role { get; set; } = "";

        public DateTime CreatedAtUtc { get; set; }

        public DateTime ExpiresAtUtc { get; set; }

        public bool IsExpired { get; set; }
    }

    /// <summary>
    /// Returned once, when an invitation is created. The token is not stored
    /// and cannot be shown again; create a new invitation if the link is lost.
    /// </summary>
    public sealed class TenantInvitationLinkDto
    {
        public TenantInvitationDto Invitation { get; set; } = new();

        public string AcceptUrl { get; set; } = "";

        public string Token { get; set; } = "";
    }

    public sealed class CreateTenantInvitationRequest
    {
        [Required]
        [EmailAddress]
        [StringLength(256)]
        public string Email { get; set; } = "";

        [Required]
        public string Role { get; set; } = "Member";
    }

    /// <summary>Token travels in the request body, not the URL, so it is not logged.</summary>
    public sealed class InvitationTokenRequest
    {
        [Required]
        public string Token { get; set; } = "";
    }

    /// <summary>What the invitee sees before accepting.</summary>
    public sealed class InvitationPreviewDto
    {
        public string TenantName { get; set; } = "";

        public string Email { get; set; } = "";

        public string Role { get; set; } = "";

        public DateTime ExpiresAtUtc { get; set; }

        /// <summary>
        /// True when an account with the invited email exists: the invitee signs
        /// in with it instead of creating a new account.
        /// </summary>
        public bool AccountExists { get; set; }
    }

    public sealed class AcceptInvitationRequest
    {
        [Required]
        public string Token { get; set; } = "";

        [Required]
        [StringLength(
            50,
            MinimumLength = 3,
            ErrorMessage = "Username must be between 3 and 50 characters.")]
        public string Username { get; set; } = "";

        [Required]
        [StringLength(
            100,
            MinimumLength = 8,
            ErrorMessage = "Password must be at least 8 characters.")]
        public string Password { get; set; } = "";
    }

    public sealed class AcceptInvitationResultDto
    {
        public string TenantName { get; set; } = "";

        public string Username { get; set; } = "";

        public string Role { get; set; } = "";

        public bool AccountCreated { get; set; }
    }
}
