using System.Security.Claims;

namespace IronForge.Shared.Services;

/// <summary>
/// The API writes tokens with JwtSecurityTokenHandler, which shortens claim
/// types (ClaimTypes.Role becomes "role", ClaimTypes.Name "unique_name").
/// Reading a token without the handler's inbound mapping returns the short
/// names, so client code maps them back before using them.
/// </summary>
public static class JwtClaims
{
    private static readonly Dictionary<string, string> ShortToLong = new(StringComparer.Ordinal)
    {
        ["role"] = ClaimTypes.Role,
        ["unique_name"] = ClaimTypes.Name,
        ["name"] = ClaimTypes.Name,
        ["nameid"] = ClaimTypes.NameIdentifier,
        ["sub"] = ClaimTypes.NameIdentifier,
        ["email"] = ClaimTypes.Email
    };

    /// <summary>Claims with the standard .NET claim types.</summary>
    public static IEnumerable<Claim> Normalize(IEnumerable<Claim> claims) =>
        claims.Select(claim =>
            ShortToLong.TryGetValue(claim.Type, out var type)
                ? new Claim(type, claim.Value, claim.ValueType, claim.Issuer)
                : claim);

    public static string? Find(IEnumerable<Claim> claims, string claimType) =>
        Normalize(claims).FirstOrDefault(x => x.Type == claimType)?.Value;
}
