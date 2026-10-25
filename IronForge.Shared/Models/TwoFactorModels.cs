using System.ComponentModel.DataAnnotations;

namespace IronForge.Shared.Models
{
    /// <summary>Second sign-in step: the code from the authenticator app, or a recovery code.</summary>
    public sealed class TwoFactorSignInRequest
    {
        [Required]
        public string TwoFactorToken { get; set; } = "";

        [Required]
        [MaxLength(20)]
        public string Code { get; set; } = "";
    }

    public sealed class TwoFactorSetupStartRequest
    {
        [Required]
        public string TwoFactorToken { get; set; } = "";
    }

    /// <summary>What the authenticator app needs: a QR code of <see cref="OtpAuthUri"/>, or the key typed in.</summary>
    public sealed class TwoFactorSetupDto
    {
        /// <summary>The secret in base32, grouped in fours for typing.</summary>
        public string ManualKey { get; set; } = "";

        public string OtpAuthUri { get; set; } = "";
    }

    public sealed class TwoFactorStatusDto
    {
        public bool Enabled { get; set; }

        /// <summary>The account must keep it on (gym admins and platform operators).</summary>
        public bool Required { get; set; }

        public DateTime? EnabledAtUtc { get; set; }

        public int RecoveryCodesLeft { get; set; }
    }

    public sealed class TwoFactorCodeRequest
    {
        [Required]
        [MaxLength(20)]
        public string Code { get; set; } = "";
    }

    public sealed class TwoFactorRecoveryCodesDto
    {
        public List<string> RecoveryCodes { get; set; } = [];
    }
}
