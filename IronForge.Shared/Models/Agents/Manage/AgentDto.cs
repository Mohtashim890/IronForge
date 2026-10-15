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

        /// <summary>System prompt sent first on every model call.</summary>
        public string Instructions { get; set; } = string.Empty;

        public int MaximumIterationsPerRequest { get; set; }

        public bool AllowMultipleToolCalls { get; set; }

        public int ApprovalLifetimeMinutes { get; set; }

        public string ModelProfile { get; set; } = string.Empty;

        public List<AgentToolBindingDto> Tools { get; set; } = [];
    }
}
