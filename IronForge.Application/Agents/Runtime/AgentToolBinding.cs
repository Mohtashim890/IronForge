using IronForge.Application.Agents.Governance;

namespace IronForge.Application.Agents.Runtime;

/// <summary>
/// Grants an agent a catalogue tool, optionally tightening its governance.
/// Null values keep the tool's defaults. Overrides can only make a tool
/// stricter (add approval, raise risk, lower the amount limit).
/// </summary>
public sealed class AgentToolBinding
{
    public required string ToolName { get; init; }

    public bool? RequiresApproval { get; init; }

    public AgentRiskLevel? RiskLevel { get; init; }

    public decimal? MaximumAmount { get; init; }
}
