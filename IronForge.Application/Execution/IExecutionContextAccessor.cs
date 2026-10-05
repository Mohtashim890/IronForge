using IronForge.Application.Agents.Models;

namespace IronForge.Application.Execution;

public interface IExecutionContextAccessor
{
    ExecutionContext? Current { get; }

    void Set(ExecutionContext context);

    void SetAgent(AgentIdentity agent);
}