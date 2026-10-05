namespace IronForge.Application.Entities
{
    public class RefreshToken
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public int? TenantId { get; set; }

        public Guid FamilyId { get; set; }

        public string TokenHash { get; set; } = "";

        public DateTime CreatedAtUtc { get; set; }

        public DateTime ExpiresAtUtc { get; set; }

        public DateTime? RevokedAtUtc { get; set; }

        public string? ReplacedByTokenHash { get; set; }

        public User User { get; set; } = null!;
        public Tenant? Tenant { get; set; }
    }
}
