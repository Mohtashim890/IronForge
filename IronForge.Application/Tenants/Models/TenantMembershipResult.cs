namespace IronForge.Application.Tenants.Models
{
    public class TenantMembershipResult
    {
        public int TenantId { get; init; }

        public string TenantName { get; init; } = "";

        public string Slug { get; init; } = "";

        public string Role { get; init; } = "";

        public bool IsActive { get; init; }
    }
}
