using System.ComponentModel.DataAnnotations;
using IronForge.Shared.Models.Agents;

namespace IronForge.Shared.Models.Agents.Manage;

public sealed class AgentEditorModel
{
    [Required]
    [StringLength(100)]
    public string AgentId { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [Required]
    [StringLength(50)]
    public string Version { get; set; } = "1.0";

    public bool IsActive { get; set; } = true;

    [Required(ErrorMessage = "Instructions are required. They define the agent's role and rules.")]
    [StringLength(AgentConfigurationLimits.MaximumInstructionsLength)]
    public string Instructions { get; set; } = string.Empty;

    [Range(
        AgentConfigurationLimits.MinimumIterationsPerRequest,
        AgentConfigurationLimits.MaximumIterationsPerRequest)]
    public int MaximumIterationsPerRequest { get; set; } =
        AgentConfigurationLimits.DefaultIterationsPerRequest;

    public bool AllowMultipleToolCalls { get; set; }

    [Range(
        AgentConfigurationLimits.MinimumApprovalLifetimeMinutes,
        AgentConfigurationLimits.MaximumApprovalLifetimeMinutes)]
    public int ApprovalLifetimeMinutes { get; set; } =
        AgentConfigurationLimits.DefaultApprovalLifetimeMinutes;

    [Required]
    [StringLength(100)]
    public string ModelProfile { get; set; } = "standard";

    public CreateAgentRequest ToCreateRequest() =>
        new()
        {
            AgentId = AgentId,
            Name = Name,
            Description = Description,
            Version = Version,
            IsActive = IsActive,
            Permissions = [],
            Instructions = Instructions,
            MaximumIterationsPerRequest = MaximumIterationsPerRequest,
            AllowMultipleToolCalls = AllowMultipleToolCalls,
            ApprovalLifetimeMinutes = ApprovalLifetimeMinutes,
            ModelProfile = ModelProfile
        };

    public UpdateAgentRequest ToUpdateRequest() =>
        new()
        {
            Name = Name,
            Description = Description,
            Version = Version,
            IsActive = IsActive,
            Instructions = Instructions,
            MaximumIterationsPerRequest = MaximumIterationsPerRequest,
            AllowMultipleToolCalls = AllowMultipleToolCalls,
            ApprovalLifetimeMinutes = ApprovalLifetimeMinutes,
            ModelProfile = ModelProfile
        };

    public static AgentEditorModel From(AgentDto agent) =>
        new()
        {
            AgentId = agent.AgentId,
            Name = agent.Name,
            Description = agent.Description,
            Version = agent.Version,
            IsActive = agent.IsActive,
            Instructions = agent.Instructions,
            MaximumIterationsPerRequest = agent.MaximumIterationsPerRequest,
            AllowMultipleToolCalls = agent.AllowMultipleToolCalls,
            ApprovalLifetimeMinutes = agent.ApprovalLifetimeMinutes,
            ModelProfile = string.IsNullOrWhiteSpace(agent.ModelProfile)
                ? "standard"
                : agent.ModelProfile
        };
}
