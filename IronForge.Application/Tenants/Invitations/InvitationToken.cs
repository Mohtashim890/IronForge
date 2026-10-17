using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace IronForge.Application.Tenants.Invitations;

/// <summary>
/// 256-bit random, URL-safe invitation tokens. Only the hash is stored, so a
/// database leak does not expose usable invitation links.
/// </summary>
public static class InvitationToken
{
    public static string Create()
    {
        return Base64Url.EncodeToString(
            RandomNumberGenerator.GetBytes(32));
    }

    public static string Hash(string token)
    {
        return Convert.ToBase64String(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(token.Trim())));
    }

    public static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }
}
