namespace IronForge.Shared.Models.Agents;

/// <summary>An active agent the current user can chat with.</summary>
public sealed class AvailableAgentDto
{
    public string AgentId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }
}
