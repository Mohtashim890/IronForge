namespace IronForge.Application.Entities
{
    /// <summary>
    /// A single-use invitation to join a tenant with a role. Only the SHA-256
    /// hash of the token is stored; the token itself is shown once to the
    /// person who created the invitation.
    /// </summary>
    public class TenantInvitation
    {
        public Guid Id { get; set; }

        public int TenantId { get; set; }

        /// <summary>Lower-case. The accepting account must have this email.</summary>
        public string Email { get; set; } = "";

        public string Role { get; set; } = "";

        public string TokenHash { get; set; } = "";

        public int? CreatedByUserId { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime ExpiresAtUtc { get; set; }

        public DateTime? AcceptedAtUtc { get; set; }

        public int? AcceptedByUserId { get; set; }

        public DateTime? RevokedAtUtc { get; set; }

        /// <summary>
        /// Concurrency token (PostgreSQL xmin), so the same invitation cannot
        /// be accepted twice by concurrent requests.
        /// </summary>
        public uint Version { get; set; }

        public bool IsPending(DateTime nowUtc) =>
            AcceptedAtUtc == null &&
            RevokedAtUtc == null &&
            ExpiresAtUtc > nowUtc;
    }
}
