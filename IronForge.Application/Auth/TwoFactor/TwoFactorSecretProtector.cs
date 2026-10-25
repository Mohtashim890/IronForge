using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace IronForge.Application.Auth.TwoFactor;

public sealed class TwoFactorOptions
{
    public const string SectionName = "TwoFactor";

    /// <summary>
    /// Encrypts authenticator secrets in the database (32+ characters). Empty
    /// uses JwtSettings:Key. Changing it makes every enrolled authenticator
    /// invalid, so those users need a two-factor reset.
    /// </summary>
    [Required]
    [MinLength(32)]
    public string EncryptionKey { get; set; } = "";

    /// <summary>Gym admins and platform operators must use an authenticator app to sign in.</summary>
    public bool RequiredForAdmins { get; set; } = true;

    /// <summary>The name authenticator apps show next to the code.</summary>
    public string Issuer { get; set; } = "IronForge";
}

/// <summary>Encrypts authenticator secrets, so a database copy alone cannot produce codes.</summary>
public interface ITwoFactorSecretProtector
{
    string Protect(byte[] secret);

    byte[] Unprotect(string protectedSecret);
}

/// <summary>AES-256-GCM with a key derived from <see cref="TwoFactorOptions.EncryptionKey"/>.</summary>
public sealed class TwoFactorSecretProtector : ITwoFactorSecretProtector
{
    private const string Prefix = "v1.";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public TwoFactorSecretProtector(IOptions<TwoFactorOptions> options)
    {
        _key = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(options.Value.EncryptionKey),
            32,
            info: Encoding.UTF8.GetBytes("IronForge.TwoFactor.v1"));
    }

    public string Protect(byte[] secret)
    {
        var output = new byte[NonceSize + TagSize + secret.Length];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, secret, output.AsSpan(NonceSize + TagSize), output.AsSpan(NonceSize, TagSize));

        return Prefix + Convert.ToBase64String(output);
    }

    public byte[] Unprotect(string protectedSecret)
    {
        if (!protectedSecret.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new CryptographicException("Unknown two-factor secret format.");
        }

        var input = Convert.FromBase64String(protectedSecret[Prefix.Length..]);
        var secret = new byte[input.Length - NonceSize - TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(
            input.AsSpan(0, NonceSize),
            input.AsSpan(NonceSize + TagSize),
            input.AsSpan(NonceSize, TagSize),
            secret);

        return secret;
    }
}
