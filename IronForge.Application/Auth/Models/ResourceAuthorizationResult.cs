namespace IronForge.Application.Auth.Models;

public class ResourceAuthorizationResult
{
    public bool Allowed { get; init; }

    public string? Reason { get; init; }

    public static ResourceAuthorizationResult Allow()
    {
        return new ResourceAuthorizationResult
        {
            Allowed = true
        };
    }

    public static ResourceAuthorizationResult Deny(
        string reason)
    {
        return new ResourceAuthorizationResult
        {
            Allowed = false,
            Reason = reason
        };
    }
}