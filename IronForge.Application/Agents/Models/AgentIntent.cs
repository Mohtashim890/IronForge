namespace IronForge.Application.Agents.Models
{
    public class AgentIntent
    {
        public string Intent { get; set; } = "";

        public string? ProductName { get; set; }

        public decimal? MaxPrice { get; set; }

        public bool RequiresTool { get; set; }
    }
}
