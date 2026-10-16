using IronForge.Shared.Models.Agents.Manage;

namespace IronForge.Application.Agents.DTOs
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

        /// <summary>
        /// Derived from the agent's tool bindings (read-only): the delegation
        /// scope ceiling for this agent.
        /// </summary>
        public List<string> Permissions { get; set; } = [];

        /// <summary>Tenant roles that may chat with the agent; empty means everyone.</summary>
        public List<string> AllowedRoles { get; set; } = [];

        /// <summary>Installed from a platform template and updated by rollouts.</summary>
        public bool IsManaged { get; set; }

        public int? TemplateVersion { get; set; }

        /// <summary>System prompt sent first on every model call.</summary>
        public string Instructions { get; set; } = string.Empty;

        public int MaximumIterationsPerRequest { get; set; }

        public bool AllowMultipleToolCalls { get; set; }

        public int ApprovalLifetimeMinutes { get; set; }

        public string ModelProfile { get; set; } = string.Empty;

        public List<AgentToolBindingDto> Tools { get; set; } = [];
    }
}
