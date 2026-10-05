using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Shared.Models
{
    public class LoginRequest
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }
}
