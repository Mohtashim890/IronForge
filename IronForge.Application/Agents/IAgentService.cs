using IronForge.Application.Agents.Models;

namespace IronForge.Application.Agents
{
    public interface IAgentService
    {
        Task<string> ChatAsync(
        string message,
        CancellationToken cancellationToken = default);

        Task<AgentIntent> AnalyzeIntentAsync(
       string message,
       CancellationToken cancellationToken = default);
    }
}
