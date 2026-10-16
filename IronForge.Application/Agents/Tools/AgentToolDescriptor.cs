using IronForge.Application.Agents.Governance;

namespace IronForge.Application.Agents.Tools;

/// <summary>
/// What a catalogue tool is and its default governance.
/// The defaults are the platform floor: an agent's binding can make them
/// stricter but never weaker (see <see cref="AgentToolRegistry"/>).
/// </summary>
public sealed class AgentToolDescriptor
{
    /// <summary>Tool name the LLM calls (snake_case by convention).</summary>
    public required string Name { get; init; }

    /// <summary>Tells the LLM when and how to use the tool.</summary>
    public required string Description { get; init; }

    /// <summary>Permission the agent must hold to use the tool.</summary>
    public required string RequiredPermission { get; init; }

    /// <summary>
    /// <see cref="Shared.Subscriptions.PlatformModules"/> id. The tool is only
    /// available to tenants whose subscription enables this module.
    /// </summary>
    public required string Module { get; init; }

    public AgentRiskLevel RiskLevel { get; init; } = AgentRiskLevel.Low;

    public bool RequiresApproval { get; init; }

    public decimal? MaximumAmount { get; init; }
}
