using System.ComponentModel.DataAnnotations.Schema;

namespace IronForge.Application.Entities
{
    public class TenantMembership
    {
        public int TenantId { get; set; }

        public int UserId { get; set; }

        public string Role { get; set; } = "Member";

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAtUtc { get; set; }
        public Tenant Tenant { get; set; } = null!;
        public User User { get; set; } = null!;
    }
}
