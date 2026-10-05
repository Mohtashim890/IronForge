using IronForge.Application.Configurations;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace IronForge.Application.Agents.Memory.Context
{
    public class ConversationContextCompactor: IConversationContextCompactor
    {
        private readonly ConversationContextOptions _options;
        private readonly IConversationTokenEstimator _tokenEstimator;

        public ConversationContextCompactor(
            IOptions<ConversationContextOptions> options,
            IConversationTokenEstimator tokenEstimator)
        {
            _options = options.Value;
            _tokenEstimator = tokenEstimator;
        }

        public ChatMessage CompactToolResult(
            ChatMessage message)
        {
            if (message.Role != ChatRole.Tool)
            {
                return message;
            }

            var resultContent =
                message.Contents
                    .OfType<FunctionResultContent>()
                    .FirstOrDefault();

            if (resultContent == null)
            {
                return message;
            }

            var resultText =
                System.Text.Json.JsonSerializer.Serialize(
                    resultContent.Result);

            var estimatedTokens =
                _tokenEstimator.EstimateTextTokens(resultText);

            if (estimatedTokens <=
                _options.MaximumToolResultTokens)
            {
                return message;
            }

            var maximumCharacters =
                _options.ToolResultHeadTokens *
                Math.Max(
                    1,
                    _options.ApproximateCharactersPerToken);

            var truncatedText =
                resultText.Length <= maximumCharacters
                    ? resultText
                    : resultText[..maximumCharacters];

            var compactedText =
                $"{truncatedText}\n\n" +
                "[Tool result compacted for model context. " +
                "The complete result remains available in durable conversation history.]";

            var compactedResult = new List<AIContent>
            {
                new FunctionResultContent(
                    resultContent.CallId,
                    compactedText)
            };

            var compactedMessage =
                new ChatMessage(
                    ChatRole.Tool,
                    compactedResult);

            return compactedMessage;
        }
    }
}
