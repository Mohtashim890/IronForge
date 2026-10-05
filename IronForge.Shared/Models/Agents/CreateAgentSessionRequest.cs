using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Shared.Models.Agents
{
    public class CreateAgentSessionRequest
    {
        public string AgentId { get; set; } = "";

        public string? Title { get; set; }
    }
}
