using IronForge.Application.Agents.Models;
using Microsoft.Extensions.AI;

namespace IronForge.Application.Agents.Tools
{
    public interface IAgentToolResolver
    {
        AIFunction Resolve(
       string toolName,
       AgentIdentity agent);
    }
}
