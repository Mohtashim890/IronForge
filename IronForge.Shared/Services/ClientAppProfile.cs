using IronForge.Shared.Authorization;

namespace IronForge.Shared.Services;

/// <summary>Who a front end is for; decides sign-in routing and which gyms are offered.</summary>
public enum ClientAudience
{
    /// <summary>Gym admins and trainers (web app).</summary>
    Staff,

    /// <summary>Gym members (mobile app).</summary>
    Member,

    /// <summary>The platform operator (platform app); works without a selected gym.</summary>
    Platform
}

/// <summary>Registered by each host so shared pages (sign-in, gym selection) behave per app.</summary>
public sealed class ClientAppProfile
{
    public required ClientAudience Audience { get; init; }

    public required string AppName { get; init; }

    /// <summary>Where users land after signing in and choosing a gym.</summary>
    public required string HomeRoute { get; init; }

    public bool AcceptsRole(string? role) => Audience switch
    {
        ClientAudience.Staff => TenantRoles.IsStaff(role),
        ClientAudience.Member => string.Equals(role, TenantRoles.Member, StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    /// <summary>Shown when the account has no gym this app serves.</summary>
    public string WrongAppMessage => Audience switch
    {
        ClientAudience.Staff => "This dashboard is for gym staff. Members use the IronForge mobile app.",
        ClientAudience.Member => "This app is for gym members. Staff use the IronForge web dashboard.",
        _ => "This app is for platform operators."
    };
}
