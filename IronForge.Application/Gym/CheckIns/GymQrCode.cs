using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace IronForge.Application.Gym.CheckIns;

/// <summary>
/// The gym's entrance code, shown by a check-in device and scanned by members:
/// <c>IFG1.{tenantId}.{deviceId}.{step}.{signature}</c>.
///
/// The API both creates and checks it, so its secret never leaves the
/// server. The step changes every 15 seconds and a code is accepted for one
/// more step, so a photo of the screen sent to someone outside stops working
/// within half a minute. It proves the member's phone was in front of that
/// screen; it is the member's own sign-in that says who they are.
/// </summary>
public sealed record GymQrCode(int TenantId, int DeviceId, long Step, string Signature)
{
    public const string Prefix = "IFG1";

    public const int StepSeconds = 15;

    private const int SignatureLength = 24;

    public static long StepAt(DateTimeOffset time) => time.ToUnixTimeSeconds() / StepSeconds;

    /// <summary>When the step that contains <paramref name="time"/> ends.</summary>
    public static DateTime StepEndsAt(DateTimeOffset time) =>
        DateTimeOffset.FromUnixTimeSeconds((StepAt(time) + 1) * StepSeconds).UtcDateTime;

    public static string Create(byte[] secret, int tenantId, int deviceId, long step)
    {
        var payload = Payload(tenantId, deviceId, step);

        return $"{payload}.{Sign(secret, payload)}";
    }

    public static bool TryParse(string? code, out GymQrCode? qr)
    {
        qr = null;

        var parts = (code ?? "").Trim().Split('.');

        if (parts.Length != 5 ||
            !string.Equals(parts[0], Prefix, StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var tenantId) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var deviceId) ||
            !long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var step) ||
            parts[4].Length != SignatureLength)
        {
            return false;
        }

        qr = new GymQrCode(tenantId, deviceId, step, parts[4].ToLowerInvariant());
        return true;
    }

    public bool HasValidSignature(byte[] secret) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Sign(secret, Payload(TenantId, DeviceId, Step))),
            Encoding.ASCII.GetBytes(Signature));

    private static string Payload(int tenantId, int deviceId, long step) =>
        string.Create(CultureInfo.InvariantCulture, $"{Prefix}.{tenantId}.{deviceId}.{step}");

    private static string Sign(byte[] secret, string payload) =>
        Convert.ToHexString(HMACSHA256.HashData(secret, Encoding.ASCII.GetBytes(payload)))
            .ToLowerInvariant()[..SignatureLength];
}
