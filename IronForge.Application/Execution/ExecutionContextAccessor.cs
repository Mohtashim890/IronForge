using IronForge.Application.Agents.Models;
using IronForge.Application.Execution;

public class ExecutionContextAccessor
    : IExecutionContextAccessor
{
    public IronForge.Application.Execution.ExecutionContext? Current { get; private set; }

    public void Set(IronForge.Application.Execution.ExecutionContext context)
    {
        if (Current != null)
        {
            throw new InvalidOperationException(
                "Execution context has already been established.");
        }

        Current = context;
    }

    public void SetAgent(AgentIdentity agent)
    {
        if (Current == null)
        {
            throw new InvalidOperationException(
                "Execution context has not been established.");
        }

        if (Current.Actor.Agent != null)
        {
            throw new InvalidOperationException(
                "An agent identity has already been established.");
        }

        Current.Actor.Agent = agent;
    }
}