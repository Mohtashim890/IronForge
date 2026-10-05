namespace IronForge.Application.Entities
{
    public class Agent
    {
        public int Id { get; set; }

        public int TenantId { get; set; }

        public string AgentId { get; set; } = "";

        public string Name { get; set; } = "";

        public string? Description { get; set; }

        public string? Version { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime UpdatedAtUtc { get; set; }

        public ICollection<AgentPermission> Permissions { get; set; }
            = new List<AgentPermission>();
    }
}
