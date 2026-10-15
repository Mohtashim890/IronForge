namespace IronForge.Application.Agents.ModelProfiles;

/// <summary>
/// A named, platform-defined model configuration an agent can use.
/// Agents reference profiles by name, never raw model ids, so the platform
/// can change models without editing every tenant's agents.
/// </summary>
public sealed record AgentModelProfile(
    string Name,
    string Model,
    int? MaxOutputTokens,
    string? Description);
