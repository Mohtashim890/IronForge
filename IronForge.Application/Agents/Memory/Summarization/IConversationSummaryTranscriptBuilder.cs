using IronForge.Application.Entities;

namespace IronForge.Application.Agents.Memory.Summarization
{
    public interface IConversationSummaryTranscriptBuilder
    {
        string Build(
        IReadOnlyList<ConversationMessage> messages);
    }
}
