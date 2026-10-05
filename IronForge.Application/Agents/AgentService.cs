using IronForge.Application.Agents.Models;
using IronForge.Application.Products.Tools.Products;
using Microsoft.Extensions.AI;

namespace IronForge.Application.Agents
{
    public class AgentService : IAgentService
    {
        private readonly IChatClient _chatClient;
        private readonly IGetProductsTool _getProductsTool;
        private readonly IGetProductTool _getProductTool;

        public AgentService(
            IChatClient chatClient,
            IGetProductsTool getProductsTool,
            IGetProductTool getProductTool)
        {
            _chatClient = chatClient;
            _getProductsTool = getProductsTool;
            _getProductTool = getProductTool;
        }

        public async Task<string> ChatAsync(
            string message,
            CancellationToken cancellationToken = default)
        {
            var messages = new List<ChatMessage>
        {
            new(
                ChatRole.System,
                """
                You are the IronForge Product Assistant.

                You help users with product-related questions.

                Be accurate, concise, and helpful.
                """),

            new(
                ChatRole.User,
                message)
        };

            var response =
                await _chatClient.GetResponseAsync(
                    messages,
                    cancellationToken: cancellationToken);

            return response.Text;
        }

        public async Task<AgentIntent> AnalyzeIntentAsync(
        string message,
        CancellationToken cancellationToken = default)
        {
            var messages = new List<ChatMessage>
        {
            new(
                ChatRole.System,
                """
                You are the IronForge Product Assistant.

                Analyze the user's request and determine their intent.

                Possible intents include:

                - product_search
                - product_lookup
                - general_question
                - unknown

                Set RequiresTool to true when the user is
                asking for information that would require
                accessing IronForge product data.

                Extract the product name when possible.

                Extract a maximum price when the user specifies one.
                """),

            new(
                ChatRole.User,
                message)
        };

            var options = new ChatOptions
            {
                ResponseFormat =
                    ChatResponseFormat.ForJsonSchema<AgentIntent>(
                        schemaName: "agent_intent",
                        schemaDescription:
                            "Structured intent extracted from a user's IronForge request.")
            };

            var response =
                await _chatClient.GetResponseAsync<AgentIntent>(
                    messages,
                    options,
                    true,
                    cancellationToken);

            return response.TryGetResult(
                       out var result)
                ? result
                : throw new InvalidOperationException(
                    "The AI response could not be parsed as AgentIntent.");
        }
    }
}
