namespace IronForge.Shared.Models.Agents.Manage;

/// <summary>
/// A catalogue tool granted to an agent, with optional overrides that can
/// only tighten the tool's defaults. Null means "use the tool's default".
/// </summary>
public sealed class AgentToolBindingDto
{
    public string ToolName { get; set; } = string.Empty;

    public bool? RequiresApproval { get; set; }

    /// <summary>Low, Medium, High or Critical.</summary>
    public string? RiskLevel { get; set; }

    public decimal? MaximumAmount { get; set; }
}
