namespace IronForge.Shared.Models.Agents.Manage;

/// <summary>Turns a built-in agent on or off for the gym.</summary>
public sealed class SetAgentActiveRequest
{
    public bool IsActive { get; set; }
}
