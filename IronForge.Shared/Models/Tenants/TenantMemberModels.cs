using System.ComponentModel.DataAnnotations;

namespace IronForge.Shared.Models.Tenants
{
    public sealed class TenantMemberDto
    {
        public int UserId { get; set; }

        public string Username { get; set; } = "";

        public string Email { get; set; } = "";

        public string Role { get; set; } = "";

        public bool IsActive { get; set; }

        public DateTime JoinedAtUtc { get; set; }

        /// <summary>The signed-in user; their own membership cannot be changed.</summary>
        public bool IsCurrentUser { get; set; }
    }

    public sealed class UpdateMemberRoleRequest
    {
        [Required]
        public string Role { get; set; } = "";
    }

    public sealed class UpdateMemberStatusRequest
    {
        public bool IsActive { get; set; }
    }
}
