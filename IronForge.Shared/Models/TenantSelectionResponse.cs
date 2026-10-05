using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Shared.Models
{
    public class TenantSelectionResponse
    {
        public string AccessToken { get; init; } = "";

        public string RefreshToken { get; init; } = "";

        public int TenantId { get; init; }

        public string TenantName { get; init; } = "";

        public string Role { get; init; } = "";
    }
}
