using Microsoft.Extensions.AI;

namespace IronForge.Application.Agents.Tools;

/// <summary>
/// Base class for catalogue tools. A tool supplies its descriptor and a
/// handler method; the handler's parameters become the JSON schema the LLM
/// fills in (DI services and CancellationToken are not exposed).
/// </summary>
public abstract class AgentTool : IAgentTool
{
    public abstract AgentToolDescriptor Descriptor { get; }

    /// <summary>The method the tool runs, e.g. <c>ExecuteAsync</c>.</summary>
    protected abstract Delegate Handler { get; }

    public AIFunction CreateFunction()
    {
        return AIFunctionFactory.Create(
            Handler,
            name: Descriptor.Name,
            description: Descriptor.Description);
    }
}
