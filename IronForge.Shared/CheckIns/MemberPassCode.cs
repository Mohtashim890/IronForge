using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace IronForge.Shared.CheckIns;

/// <summary>
/// The text inside a member's rotating QR pass:
/// <c>IF1.{tenantId}.{memberId}.{keyVersion}.{step}.{signature}</c>.
///
/// The member app computes it offline from the pass secret and the clock
/// (a new <c>step</c> every 30 seconds, like a TOTP code); the server derives
/// the same secret and checks the signature. A screenshot therefore stops
/// working within about a minute, and the server refuses a step it has
/// already seen for the member, so a copied code works at most once.
/// </summary>
public sealed record MemberPassCode(
    int TenantId,
    int MemberId,
    int KeyVersion,
    long Step,
    string Signature)
{
    public const string Prefix = "IF1";

    public const int DefaultStepSeconds = 30;

    /// <summary>Hex characters of the HMAC kept in the code (96 bits).</summary>
    private const int SignatureLength = 24;

    public static long StepAt(DateTimeOffset time, int stepSeconds = DefaultStepSeconds) =>
        time.ToUnixTimeSeconds() / stepSeconds;

    [UnsupportedOSPlatform("browser")]
    public static string Create(byte[] secret, int tenantId, int memberId, int keyVersion, long step)
    {
        var payload = Payload(tenantId, memberId, keyVersion, step);

        return $"{payload}.{Sign(secret, payload)}";
    }

    public static bool TryParse(string? code, out MemberPassCode? pass)
    {
        pass = null;

        var parts = (code ?? "").Trim().Split('.');

        if (parts.Length != 6 ||
            !string.Equals(parts[0], Prefix, StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var tenantId) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var memberId) ||
            !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var keyVersion) ||
            !long.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out var step) ||
            parts[5].Length != SignatureLength)
        {
            return false;
        }

        pass = new MemberPassCode(tenantId, memberId, keyVersion, step, parts[5].ToLowerInvariant());
        return true;
    }

    /// <summary>Constant-time comparison with the signature the secret produces.</summary>
    [UnsupportedOSPlatform("browser")]
    public bool HasValidSignature(byte[] secret)
    {
        var expected = Encoding.ASCII.GetBytes(Sign(secret, Payload(TenantId, MemberId, KeyVersion, Step)));
        var actual = Encoding.ASCII.GetBytes(Signature);

        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static string Payload(int tenantId, int memberId, int keyVersion, long step) =>
        string.Create(CultureInfo.InvariantCulture, $"{Prefix}.{tenantId}.{memberId}.{keyVersion}.{step}");

    [UnsupportedOSPlatform("browser")]
    private static string Sign(byte[] secret, string payload)
    {
        var hash = HMACSHA256.HashData(secret, Encoding.ASCII.GetBytes(payload));

        return Convert.ToHexString(hash).ToLowerInvariant()[..SignatureLength];
    }
}
