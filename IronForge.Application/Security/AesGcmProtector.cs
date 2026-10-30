using System.Security.Cryptography;
using System.Text;

namespace IronForge.Application.Security;

/// <summary>
/// AES-256-GCM encryption for secrets kept in the database (authenticator
/// secrets, gyms' payment provider keys). The key is derived from a
/// configured master key and a purpose, so each use gets its own key.
/// Format: "v1." + base64(nonce | tag | ciphertext).
/// </summary>
public sealed class AesGcmProtector
{
    private const string Prefix = "v1.";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public AesGcmProtector(string masterKey, string purpose)
    {
        _key = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(masterKey),
            32,
            info: Encoding.UTF8.GetBytes(purpose));
    }

    public string Protect(byte[] plaintext)
    {
        var output = new byte[NonceSize + TagSize + plaintext.Length];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintext, output.AsSpan(NonceSize + TagSize), output.AsSpan(NonceSize, TagSize));

        return Prefix + Convert.ToBase64String(output);
    }

    public byte[] Unprotect(string protectedValue)
    {
        if (!protectedValue.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new CryptographicException("Unknown protected value format.");
        }

        var input = Convert.FromBase64String(protectedValue[Prefix.Length..]);
        var plaintext = new byte[input.Length - NonceSize - TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(
            input.AsSpan(0, NonceSize),
            input.AsSpan(NonceSize + TagSize),
            input.AsSpan(NonceSize, TagSize),
            plaintext);

        return plaintext;
    }

    public string ProtectString(string value) => Protect(Encoding.UTF8.GetBytes(value));

    public string UnprotectString(string protectedValue) => Encoding.UTF8.GetString(Unprotect(protectedValue));
}
