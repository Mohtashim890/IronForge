namespace IronForge.Application.Entities
{
    public sealed class AgentMcpClientAuthorization
    {
        public int Id { get; set; }

        public int TenantId { get; set; }

        public int AgentEntityId { get; set; }

        public int McpClientEntityId { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime UpdatedAtUtc { get; set; }

        public Agent Agent { get; set; } = null!;

        public McpClient McpClient { get; set; } = null!;
    }
}
