namespace IronForge.Shared.Models.Agents.Manage;

/// <summary>A tool available in the platform's agent tool catalogue.</summary>
public sealed class AgentToolCatalogItemDto
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string RequiredPermission { get; set; } = string.Empty;

    /// <summary>Subscription module the tool belongs to.</summary>
    public string Module { get; set; } = string.Empty;

    public string ModuleName { get; set; } = string.Empty;

    /// <summary>Default risk level: Low, Medium, High or Critical.</summary>
    public string RiskLevel { get; set; } = string.Empty;

    public bool RequiresApproval { get; set; }

    public decimal? MaximumAmount { get; set; }
}
