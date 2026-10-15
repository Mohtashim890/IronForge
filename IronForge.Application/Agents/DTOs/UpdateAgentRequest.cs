using System.ComponentModel.DataAnnotations;
using IronForge.Shared.Models.Agents.Manage;

namespace IronForge.Application.Agents.DTOs
{
    public sealed class UpdateAgentRequest
    {
        [Required]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;

        [StringLength(2000)]
        public string? Description { get; set; }

        [Required]
        [StringLength(50)]
        public string Version { get; set; } = "1.0";

        public bool IsActive { get; set; }

        // Behaviour settings. Null keeps the current value, so clients that
        // predate these fields cannot wipe them.
        [StringLength(AgentConfigurationLimits.MaximumInstructionsLength)]
        public string? Instructions { get; set; }

        [Range(
            AgentConfigurationLimits.MinimumIterationsPerRequest,
            AgentConfigurationLimits.MaximumIterationsPerRequest)]
        public int? MaximumIterationsPerRequest { get; set; }

        public bool? AllowMultipleToolCalls { get; set; }

        [Range(
            AgentConfigurationLimits.MinimumApprovalLifetimeMinutes,
            AgentConfigurationLimits.MaximumApprovalLifetimeMinutes)]
        public int? ApprovalLifetimeMinutes { get; set; }

        /// <summary>Platform model profile name; null keeps the current/default profile.</summary>
        [StringLength(100)]
        public string? ModelProfile { get; set; }
    }
}
