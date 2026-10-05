using IronForge.Application.Configurations;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace IronForge.Application.Agents.Memory.Context
{
    public class ConversationTokenEstimator
     : IConversationTokenEstimator
    {
        private readonly ConversationContextOptions _options;

        public ConversationTokenEstimator(
            IOptions<ConversationContextOptions> options)
        {
            _options = options.Value;
        }

        public int EstimateTextTokens(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            var charactersPerToken =
                Math.Max(
                    1,
                    _options.ApproximateCharactersPerToken);

            return (int)Math.Ceiling(
                text.Length /
                (double)charactersPerToken);
        }

        public int EstimateMessageTokens(
            ChatMessage message)
        {
            var total = 4; // approximate message overhead

            foreach (var content in message.Contents)
            {
                total += EstimateContentTokens(content);
            }

            return total;
        }

        private int EstimateContentTokens(
            AIContent content)
        {
            return content switch
            {
                TextContent text =>
                    EstimateTextTokens(text.Text),

                FunctionCallContent functionCall =>
                    EstimateTextTokens(
                        functionCall.Name) +
                    EstimateTextTokens(
                        System.Text.Json.JsonSerializer
                            .Serialize(functionCall.Arguments)) +
                    10,

                FunctionResultContent functionResult =>
                    EstimateTextTokens(
                        System.Text.Json.JsonSerializer
                            .Serialize(functionResult.Result)) +
                    10,

                _ => 50
            };
        }
    }
}
