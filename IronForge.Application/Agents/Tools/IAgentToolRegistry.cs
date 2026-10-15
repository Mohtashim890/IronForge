using IronForge.Application.Agents.Governance;
using IronForge.Application.Agents.Models;
using IronForge.Application.Agents.Runtime;
using Microsoft.Extensions.AI;

namespace IronForge.Application.Agents.Tools;

public interface IAgentToolRegistry
{
    /// <summary>All tools in the catalogue, ordered by name.</summary>
    IReadOnlyList<AgentToolDescriptor> GetCatalogue();

    /// <summary>The catalogue entry for a tool, or null when not registered.</summary>
    AgentToolDescriptor? FindDescriptor(string toolName);

    /// <summary>
    /// The single source of a tool's governance for an agent: the catalogue
    /// defaults combined with the agent's binding. Throws if the agent is
    /// not bound to the tool or the tool is not in the catalogue.
    /// </summary>
    AgentToolPolicy GetEffectivePolicy(
        AgentDefinition definition,
        string toolName);

    /// <summary>
    /// Governed tools offered to the model. Tools whose effective policy
    /// requires approval are wrapped so the model's call pauses for a human.
    /// </summary>
    IList<AITool> CreateModelTools(
        AgentDefinition definition,
        AgentIdentity agent);

    /// <summary>
    /// Governed tool used to execute a call a human has already approved.
    /// </summary>
    AIFunction CreateApprovedExecutionTool(
        AgentDefinition definition,
        AgentIdentity agent,
        string toolName);
}
