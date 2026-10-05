namespace IronForge.Shared.Models.Agents.Manage
{
    public sealed class AgentDto
    {
        public int Id { get; set; }

        public string AgentId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string Version { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime UpdatedAtUtc { get; set; }

        public List<string> Permissions { get; set; } = [];
    }
}
