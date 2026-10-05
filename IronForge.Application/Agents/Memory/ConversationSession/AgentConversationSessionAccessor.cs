using IronForge.Application.Entities;

namespace IronForge.Application.Agents.Memory.ConversationSession
{
    public class AgentConversationSessionAccessor
    : IAgentConversationSessionAccessor
    {
        public AgentSession? Current { get; private set; }

        public void Set(AgentSession session)
        {
            if (Current != null)
            {
                throw new InvalidOperationException(
                    "Agent conversation session has already been established.");
            }

            Current = session;
        }

        public void Clear()
        {
            Current = null;
        }
    }
}
