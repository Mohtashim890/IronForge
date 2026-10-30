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
    /// Master key for member pass secrets. When empty the
    /// JWT signing key is used. Changing it invalidates every member's pass
    /// until the app downloads the new one (which it does whenever it is online).
    /// </summary>
    [Required]
    [MinLength(32)]
    public string PassSigningKey { get; set; } = "";
}

/// <summary>
/// Derives each member's pass secret from the master key, the gym, the member
/// and the pass version. Nothing is stored per member, and one member's
/// secret says nothing about another's.
/// </summary>
public interface ICheckInKeys
{
    byte[] MemberPassSecret(int tenantId, int memberId, int keyVersion);
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

    private byte[] Derive(string label) =>
        HMACSHA256.HashData(_masterKey, Encoding.ASCII.GetBytes(label));
}
