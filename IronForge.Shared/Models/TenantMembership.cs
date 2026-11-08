using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Shared.Models
{
    public class TenantMembership
    {
        public int TenantId { get; init; }

        public string TenantName { get; init; } = "";

        public string Slug { get; init; } = "";

        public string Role { get; init; } = "";

        public bool IsActive { get; init; }

        /// <summary>Why the gym cannot be opened (suspended, trial ended…); null when IsActive.</summary>
        public string? UnavailableReason { get; init; }
    }
}
