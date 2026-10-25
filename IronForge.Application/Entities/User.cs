namespace IronForge.Application.Entities
{
    public class User
    {
        public int Id { get; set; }

        public string Username { get; set; } = "";

        public string Email { get; set; } = "";

        public string PasswordHash { get; set; } = "";

        public string Role { get; set; } = "User";
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAtUtc { get; set; }

        public DateTime? LastLoginAtUtc { get; set; }

        /// <summary>
        /// Copied into every access token. Changing it (password reset, two-factor
        /// reset) ends every session at once, before the tokens expire.
        /// </summary>
        public string SecurityStamp { get; set; } = NewSecurityStamp();

        /// <summary>Failed sign-in attempts in a row (password or two-factor code).</summary>
        public int FailedSignInCount { get; set; }

        /// <summary>Sign-in is refused until then, even with the right password.</summary>
        public DateTime? LockoutEndUtc { get; set; }

        public bool TwoFactorEnabled { get; set; }

        /// <summary>The authenticator app secret, encrypted (ITwoFactorSecretProtector).</summary>
        public string? TwoFactorSecret { get; set; }

        /// <summary>A secret being set up, until the first code from the app confirms it.</summary>
        public string? TwoFactorPendingSecret { get; set; }

        /// <summary>Last accepted code's time step: a code works once.</summary>
        public long? TwoFactorLastStep { get; set; }

        public DateTime? TwoFactorEnabledAtUtc { get; set; }

        /// <summary>SHA-256 of the unused one-time recovery codes.</summary>
        public List<string> TwoFactorRecoveryCodeHashes { get; set; } = [];

        public static string NewSecurityStamp() => Guid.NewGuid().ToString("N");
    }
}
