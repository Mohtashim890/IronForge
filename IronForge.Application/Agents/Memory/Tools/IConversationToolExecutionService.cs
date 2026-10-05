using Microsoft.Extensions.AI;

namespace IronForge.Application.Agents.Memory.Tools
{
    public interface IConversationToolExecutionService
    {
        ValueTask<object?> InvokeAsync(
        FunctionInvocationContext context,
        CancellationToken cancellationToken = default);
    }
}
