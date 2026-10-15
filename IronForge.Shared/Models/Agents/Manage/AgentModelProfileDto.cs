namespace IronForge.Shared.Models.Agents.Manage;

public sealed class AgentModelProfileDto
{
    public string Name { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public int? MaxOutputTokens { get; set; }

    public string? Description { get; set; }
}
