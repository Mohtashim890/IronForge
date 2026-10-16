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

        /// <summary>System prompt sent first on every model call.</summary>
        public string Instructions { get; set; } = "";

        public int MaximumIterationsPerRequest { get; set; } = 5;

        public bool AllowMultipleToolCalls { get; set; }

        public int ApprovalLifetimeMinutes { get; set; } = 10;

        /// <summary>Platform model profile name (AISettings:ModelProfiles).</summary>
        public string ModelProfile { get; set; } = "standard";

        /// <summary>
        /// Tenant roles whose members may chat with the agent. Empty: every member.
        /// </summary>
        public List<string> AllowedRoles { get; set; } = [];

        /// <summary>
        /// The platform template this agent was installed from. Set: the agent is
        /// managed (updated by template rollouts, not editable by the tenant
        /// beyond active state and allowed roles). Null: tenant-built or detached.
        /// </summary>
        public string? TemplateId { get; set; }

        /// <summary>Template version currently installed.</summary>
        public int? TemplateVersion { get; set; }

        public bool IsManaged => TemplateId != null;

        public ICollection<AgentToolBindingEntity> ToolBindings { get; set; }
            = new List<AgentToolBindingEntity>();

    }
}
