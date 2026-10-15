namespace IronForge.Shared.Models.Agents.Manage;

/// <summary>
/// Platform limits for tenant-configurable agent settings. Shared by the API
/// (request validation) and the UI (form validation) so both enforce the same
/// bounds.
/// </summary>
public static class AgentConfigurationLimits
{
    /// <summary>
    /// Matches the context budget's SystemPromptReserveTokens (1500) at
    /// roughly four characters per token.
    /// </summary>
    public const int MaximumInstructionsLength = 6000;

    public const int MinimumIterationsPerRequest = 1;
    public const int MaximumIterationsPerRequest = 10;
    public const int DefaultIterationsPerRequest = 5;

    public const int MinimumApprovalLifetimeMinutes = 1;
    public const int MaximumApprovalLifetimeMinutes = 1440;
    public const int DefaultApprovalLifetimeMinutes = 10;
}
