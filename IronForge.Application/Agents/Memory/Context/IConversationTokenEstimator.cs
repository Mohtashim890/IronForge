namespace IronForge.Application.Agents.Memory.Context
{
    public interface IConversationTokenEstimator
    {
        int EstimateTextTokens(string? text);

        int EstimateMessageTokens(
            Microsoft.Extensions.AI.ChatMessage message);
    }
}
