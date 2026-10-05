using System.ComponentModel.DataAnnotations;

namespace IronForge.Application.Agents.DTOs
{
    public class AgentChatRequest
    {
        public Guid? SessionId { get; set; } = Guid.Empty;

        [Required]
        public string Message { get; set; } = "";
    }
}
