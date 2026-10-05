using Microsoft.Extensions.AI;

namespace IronForge.Application.Agents.Memory.Context
{
    public interface IConversationContextCompactor
    {
        ChatMessage CompactToolResult(
       ChatMessage message);
    }
}
