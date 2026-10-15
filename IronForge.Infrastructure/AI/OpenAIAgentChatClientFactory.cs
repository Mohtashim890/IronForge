using System.ClientModel;
using System.Collections.Concurrent;
using IronForge.Application.Agents.ModelProfiles;
using IronForge.Application.Configurations;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;

namespace IronForge.Infrastructure.AI;

/// <summary>
/// Builds OpenAI chat clients for the platform's model profiles
/// (AISettings:ModelProfiles). One OpenAIClient (connection pool) is shared;
/// one IChatClient is created per profile on first use and reused for the
/// lifetime of the process. Registered as a singleton.
/// </summary>
public sealed class OpenAIAgentChatClientFactory : IAgentChatClientFactory
{
    private readonly IReadOnlyDictionary<string, AgentModelProfile> _profiles;
    private readonly ConcurrentDictionary<string, IChatClient> _clients =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly OpenAIClient _openAIClient;
    private readonly ILogger<OpenAIAgentChatClientFactory> _logger;

    public OpenAIAgentChatClientFactory(
        IOptions<AISettings> options,
        ILogger<OpenAIAgentChatClientFactory> logger)
    {
        var settings = options.Value;

        _logger = logger;
        _openAIClient = new OpenAIClient(new ApiKeyCredential(settings.ApiKey));

        var profiles =
            new Dictionary<string, AgentModelProfile>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, profile) in settings.ModelProfiles)
        {
            profiles[name] = new AgentModelProfile(
                Name: name.ToLowerInvariant(),
                Model: string.IsNullOrWhiteSpace(profile.Model) ? settings.Model : profile.Model,
                MaxOutputTokens: profile.MaxOutputTokens,
                Description: profile.Description);
        }

        // "standard" always exists so agents and the fallback have a target.
        profiles.TryAdd(
            IAgentChatClientFactory.StandardProfile,
            new AgentModelProfile(
                IAgentChatClientFactory.StandardProfile,
                settings.Model,
                MaxOutputTokens: null,
                Description: "Default model."));

        _profiles = profiles;
    }

    public IReadOnlyList<AgentModelProfile> GetProfiles()
    {
        return _profiles.Values
            .OrderBy(profile => profile.Name, StringComparer.Ordinal)
            .ToList();
    }

    public bool HasProfile(string profileName)
    {
        return _profiles.ContainsKey(profileName);
    }

    public (IChatClient Client, AgentModelProfile Profile) GetClient(string? profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName) ||
            !_profiles.TryGetValue(profileName, out var profile))
        {
            if (!string.IsNullOrWhiteSpace(profileName))
            {
                _logger.LogWarning(
                    "Model profile {ModelProfile} is not configured; using the standard profile.",
                    profileName);
            }

            profile = _profiles[IAgentChatClientFactory.StandardProfile];
        }

        var client = _clients.GetOrAdd(profile.Name, _ => CreateClient(profile));

        return (client, profile);
    }

    private IChatClient CreateClient(AgentModelProfile profile)
    {
#pragma warning disable OPENAI001
        return _openAIClient
            .GetResponsesClient()
            .AsIChatClient(profile.Model);
#pragma warning restore OPENAI001
    }
}
