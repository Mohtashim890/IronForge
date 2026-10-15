using Microsoft.Extensions.AI;

namespace IronForge.Application.Agents.Tools;

/// <summary>
/// A capability in the tool catalogue. Tools contain only business logic;
/// governance and approval are added by <see cref="AgentToolRegistry"/>.
/// </summary>
public interface IAgentTool
{
    AgentToolDescriptor Descriptor { get; }

    /// <summary>
    /// Creates the raw (ungoverned) function. Never offer this directly to
    /// a model; always go through <see cref="IAgentToolRegistry"/>.
    /// </summary>
    AIFunction CreateFunction();
}
