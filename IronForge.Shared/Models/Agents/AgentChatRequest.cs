using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace IronForge.Shared.Models.Agents
{
    public class AgentChatRequest
    {
        public Guid? SessionId { get; set; } = Guid.Empty;

        [Required]
        public string Message { get; set; } = "";
    }
}
