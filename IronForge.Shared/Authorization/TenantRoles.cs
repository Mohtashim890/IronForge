namespace IronForge.Shared.Authorization
{
    /// <summary>
    /// Fixed roles a user can hold inside one gym (TenantMembership.Role).
    /// Their permissions are defined by RolePermissions in the API.
    /// </summary>
    public static class TenantRoles
    {
        /// <summary>Gym owner or manager: everything in the gym, including billing and staff.</summary>
        public const string Admin = "Admin";

        /// <summary>Staff: members, check-ins and routines; no billing or settings.</summary>
        public const string Trainer = "Trainer";

        /// <summary>A gym member using the mobile app: only their own data.</summary>
        public const string Member = "Member";

        public static readonly IReadOnlyList<string> All =
            [Admin, Trainer, Member];

        /// <summary>Roles that work in the gym admin web app.</summary>
        public static readonly IReadOnlyList<string> Staff =
            [Admin, Trainer];

        public static bool IsStaff(string? role) =>
            Staff.Contains(Normalize(role) ?? "");

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
