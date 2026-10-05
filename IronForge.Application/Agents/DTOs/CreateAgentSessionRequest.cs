namespace IronForge.Application.Agents.DTOs
{
    public class CreateAgentSessionRequest
    {
        public string AgentId { get; set; } = "";

        public string? Title { get; set; }
    }
}
