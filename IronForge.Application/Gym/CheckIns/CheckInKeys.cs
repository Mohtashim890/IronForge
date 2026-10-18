using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace IronForge.Application.Gym.CheckIns;

public sealed class CheckInOptions
{
    public const string SectionName = "CheckIns";

    /// <summary>
    /// Master key for member pass and entrance code secrets. When empty the
    /// JWT signing key is used. Changing it invalidates every member's pass
    /// until the app downloads the new one (which it does whenever it is online).
    /// </summary>
    [Required]
    [MinLength(32)]
    public string PassSigningKey { get; set; } = "";
}

/// <summary>
/// Derives the HMAC secrets of check-in codes from the master key. Nothing is
/// stored per member or device, and one secret says nothing about another:
/// <list type="bullet">
/// <item>a member's pass secret, per gym, member and pass version; the app gets it;</item>
/// <item>a check-in device's entrance-code secret, per gym and device; it never leaves the API.</item>
/// </list>
/// </summary>
public interface ICheckInKeys
{
    byte[] MemberPassSecret(int tenantId, int memberId, int keyVersion);

    byte[] GymQrSecret(int tenantId, int deviceId);
}

public sealed class CheckInKeys : ICheckInKeys
{
    private readonly byte[] _masterKey;

    public CheckInKeys(IOptions<CheckInOptions> options)
    {
        _masterKey = SHA256.HashData(
            Encoding.UTF8.GetBytes("ironforge-member-pass|" + options.Value.PassSigningKey));
    }

    public byte[] MemberPassSecret(int tenantId, int memberId, int keyVersion) =>
        Derive(string.Create(CultureInfo.InvariantCulture, $"{tenantId}|{memberId}|{keyVersion}"));

    // A different prefix: an entrance-code secret can never equal a pass secret.
    public byte[] GymQrSecret(int tenantId, int deviceId) =>
        Derive(string.Create(CultureInfo.InvariantCulture, $"gym-qr|{tenantId}|{deviceId}"));

    private byte[] Derive(string label) =>
        HMACSHA256.HashData(_masterKey, Encoding.ASCII.GetBytes(label));
}
