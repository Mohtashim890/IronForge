using Microsoft.Extensions.AI;

namespace IronForge.Application.Agents.ModelProfiles;

public interface IAgentChatClientFactory
{
    public const string StandardProfile = "standard";

    /// <summary>All configured profiles, ordered by name.</summary>
    IReadOnlyList<AgentModelProfile> GetProfiles();

    bool HasProfile(string profileName);

    /// <summary>
    /// The client and profile for <paramref name="profileName"/>. An unknown
    /// profile falls back to the standard profile (and is logged), so a
    /// profile removed from configuration does not take agents offline.
    /// </summary>
    (IChatClient Client, AgentModelProfile Profile) GetClient(string? profileName);
}
