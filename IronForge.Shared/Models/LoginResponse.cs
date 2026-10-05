using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Shared.Models
{
    public class LoginResponse
    {
        public string AccessToken { get; set; } = "";

        public string RefreshToken { get; set; } = "";
    }
}
