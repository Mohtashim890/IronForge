namespace IronForge.Shared.Authorization
{
    /// <summary>
    /// Fixed roles a user can hold inside one tenant (TenantMembership.Role).
    /// Their permissions are defined by RolePermissions in the API.
    /// </summary>
    public static class TenantRoles
    {
        public const string Admin = "Admin";
        public const string Member = "Member";
        public const string Viewer = "Viewer";

        public static readonly IReadOnlyList<string> All =
            [Admin, Member, Viewer];

        /// <summary>
        /// Returns the canonical spelling of a known role, or null.
        /// </summary>
        public static string? Normalize(string? role)
        {
            return All.FirstOrDefault(
                x => string.Equals(
                    x,
                    role?.Trim(),
                    StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Account-wide roles (User.Role). They grant platform permissions only;
    /// tenant access always comes from <see cref="TenantRoles"/>.
    /// </summary>
    public static class PlatformRoles
    {
        /// <summary>Operates the platform: provisions and manages tenants.</summary>
        public const string PlatformAdmin = "PlatformAdmin";

        public const string User = "User";
    }
}
