using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Shared.Models
{
    public class ActiveTenant
    {
        public int TenantId { get; init; }

        public string TenantName { get; init; } = "";

        public string Role { get; init; } = "";
    }
}
