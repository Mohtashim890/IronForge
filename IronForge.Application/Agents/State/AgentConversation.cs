using Microsoft.Extensions.AI;

namespace IronForge.Application.Agents.State
{
    public class AgentConversation
    {
        public string SessionId { get; set; } = "";

        public int UserId { get; set; }

        public string AgentId { get; set; } = "";

        public List<ChatMessage> Messages { get; set; } = [];

        public DateTime CreatedAtUtc { get; set; }

        public DateTime LastActivityAtUtc { get; set; }
    }
}
