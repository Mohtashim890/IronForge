using IronForge.Application.Entities;

namespace IronForge.Application.Agents.Memory.ConversationSession
{
    public interface IConversationAccessService
    {
        ConversationAccessResult AuthorizeSession(
            AgentSession session);
    }
}