namespace IronForge.Application.Agents.Models
{
    public class AgentIdentity
    {
        public string AgentId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public AgentType Type { get; set; }
    }
    public enum AgentType
    {
        Internal,
        External
    }
}
