using IronForge.Application.Entities;

namespace IronForge.Application.Agents.Memory.Context
{
    public interface IConversationContextUnitBuilder
    {
        IReadOnlyList<IReadOnlyList<ConversationMessage>>
        BuildUnits(
            IReadOnlyList<ConversationMessage> history);
    }
}
