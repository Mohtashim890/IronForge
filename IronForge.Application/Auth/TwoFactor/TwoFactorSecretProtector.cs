using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using IronForge.Application.Security;
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
    private readonly AesGcmProtector _protector;

    public TwoFactorSecretProtector(IOptions<TwoFactorOptions> options)
    {
        _protector = new AesGcmProtector(options.Value.EncryptionKey, "IronForge.TwoFactor.v1");
    }

    public string Protect(byte[] secret) => _protector.Protect(secret);

    public byte[] Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);
}
