namespace IronForge.Application.Tenants.Models
{
    public class TenantSelectionResult
    {
        public bool Succeeded { get; init; }

        public int? TenantId { get; init; }

        public string? TenantName { get; init; }

        public string? Role { get; init; }

        public string? Error { get; init; }

        public static TenantSelectionResult Success(
            int tenantId,
            string tenantName,
            string role)
        {
            return new TenantSelectionResult
            {
                Succeeded = true,
                TenantId = tenantId,
                TenantName = tenantName,
                Role = role
            };
        }

        public static TenantSelectionResult Failure(string error)
        {
            return new TenantSelectionResult
            {
                Succeeded = false,
                Error = error
            };
        }
    }
}
