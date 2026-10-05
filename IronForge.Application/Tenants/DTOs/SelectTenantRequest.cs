namespace IronForge.Application.Tenants.DTOs
{
    public class SelectTenantRequest
    {
        public int TenantId { get; set; }

        public string RefreshToken { get; set; } = "";
    }
}
