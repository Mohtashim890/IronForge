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

    public CreateAgentRequest ToCreateRequest() =>
        new()
        {
            AgentId = AgentId,
            Name = Name,
            Description = Description,
            Version = Version,
            IsActive = IsActive,
            Permissions = []
        };

    public UpdateAgentRequest ToUpdateRequest() =>
        new()
        {
            Name = Name,
            Description = Description,
            Version = Version,
            IsActive = IsActive
        };

    public static AgentEditorModel From(AgentDto agent) =>
        new()
        {
            AgentId = agent.AgentId,
            Name = agent.Name,
            Description = agent.Description,
            Version = agent.Version,
            IsActive = agent.IsActive
        };
}
