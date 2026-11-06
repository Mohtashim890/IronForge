namespace IronForge.Application.Agents.Runtime;

/// <summary>
/// The agent or conversation cannot be used: the agent is turned off, not in
/// the gym's plan, or the session does not exist for it. The message is meant
/// for the user; the API maps this to HTTP 404.
/// </summary>
public sealed class AgentUnavailableException : InvalidOperationException
{
    public AgentUnavailableException(string message)
        : base(message)
    {
    }
}
