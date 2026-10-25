using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Shared.Models
{
    public class LoginResponse
    {
        public string AccessToken { get; set; } = "";

        public string RefreshToken { get; set; } = "";

        /// <summary>The password was right; a code from the authenticator app is needed next (no tokens yet).</summary>
        public bool TwoFactorRequired { get; set; }

        /// <summary>The account must set up an authenticator app before it can sign in.</summary>
        public bool TwoFactorSetupRequired { get; set; }

        /// <summary>Proves the password step for a few minutes; sent with the code.</summary>
        public string? TwoFactorToken { get; set; }

        /// <summary>One-time recovery codes, returned once when two-factor sign-in was just set up.</summary>
        public List<string>? RecoveryCodes { get; set; }
    }
}
