using IronForge.Application.Agents.Memory.ConversationSession;
using IronForge.Application.Entities;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace IronForge.Application.Agents.Memory.Tools
{
    public class ConversationMessageMapper
    {
        private readonly IConversationToolContentMapper _toolContentMapper;

        public ConversationMessageMapper(
            IConversationToolContentMapper toolContentMapper)
        {
            _toolContentMapper = toolContentMapper;
        }

        public ChatMessage ToChatMessage(
            ConversationMessage message)
        {
            return message.Role switch
            {
                ConversationMessageRole.System =>
                    new ChatMessage(
                        ChatRole.System,
                        message.Content),

                ConversationMessageRole.User =>
                    new ChatMessage(
                        ChatRole.User,
                        message.Content),

                ConversationMessageRole.Assistant =>
                    MapAssistantMessage(message),

                ConversationMessageRole.Tool =>
                    MapToolMessage(message),

                _ => throw new InvalidOperationException(
                    $"Unsupported conversation role: {message.Role}.")
            };
        }

        private ChatMessage MapAssistantMessage(
            ConversationMessage message)
        {
            if (string.Equals(
                GetContentType(message),
                "functionCall",
                StringComparison.OrdinalIgnoreCase))
            {
                var functionCall =
                    _toolContentMapper.DeserializeFunctionCall(
                        message.Content);

                return new ChatMessage(
                    ChatRole.Assistant,
                    new List<AIContent>
                    {
                    functionCall
                    });
            }

            return new ChatMessage(
                ChatRole.Assistant,
                message.Content);
        }

        private ChatMessage MapToolMessage(
            ConversationMessage message)
        {
            if (!string.Equals(
                GetContentType(message),
                "functionResult",
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Tool conversation message does not contain " +
                    "function result metadata.");
            }

            var functionResult =
                _toolContentMapper.DeserializeFunctionResult(
                    message.Content);

            return new ChatMessage(
                ChatRole.Tool,
                new List<AIContent>
                {
                functionResult
                });
        }

        private static string? GetContentType(
            ConversationMessage message)
        {
            if (string.IsNullOrWhiteSpace(message.MetadataJson))
                return null;

            using var document =
                System.Text.Json.JsonDocument.Parse(
                    message.MetadataJson);

            if (document.RootElement.TryGetProperty(
                "contentType",
                out var property))
            {
                return property.GetString();
            }

            return null;
        }
    }
}