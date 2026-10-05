using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Shared.Models.Agents
{
    public class AgentSessionDto
    {
        public Guid Id { get; set; }

        public string AgentId { get; set; } = "";

        public string? Title { get; set; }

        public AgentSessionStatus Status { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime UpdatedAtUtc { get; set; }

        public DateTime? LastMessageAtUtc { get; set; }
    }
}
