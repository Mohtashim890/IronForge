using IronForge.Application.Entities;

namespace IronForge.Application.Agents.Memory.ConversationSession
{
    public interface IAgentConversationSessionAccessor
    {
        AgentSession? Current { get; }

        void Set(AgentSession session);

        void Clear();
    }
}
