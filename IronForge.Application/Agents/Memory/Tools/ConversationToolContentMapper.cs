using Microsoft.Extensions.AI;
using System.Text.Json;

namespace IronForge.Application.Agents.Memory.Tools
{
    public class ConversationToolContentMapper : IConversationToolContentMapper
    {
        public AIFunctionArguments DeserializeFunctionArguments(
        AIFunction function,
        string argumentsJson)
        {
            using var document =
                JsonDocument.Parse(argumentsJson);

            if (document.RootElement.ValueKind !=
                JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    "Approved tool arguments must be a JSON object.");
            }

            var arguments = new AIFunctionArguments();

            foreach (var property in document.RootElement.EnumerateObject())
            {
                arguments[property.Name] =
                    property.Value.Clone();
            }

            return arguments;
        }

        public FunctionResultContent DeserializeFunctionResult(string json)
        {
            var model =
                JsonSerializer.Deserialize<ToolCallPersistenceModel>(json)
                ?? throw new InvalidOperationException(
                    "Unable to deserialize persisted function result.");

            if (string.IsNullOrWhiteSpace(model.CallId))
                throw new InvalidOperationException(
                    "Persisted function result does not contain a call ID.");

            return new FunctionResultContent(
                model.CallId,
                model.Result);
        }

        public FunctionCallContent DeserializeFunctionCall(string json)
        {
            var model =
                JsonSerializer.Deserialize<ToolCallPersistenceModel>(json)
                ?? throw new InvalidOperationException(
                    "Unable to deserialize persisted function call.");

            if (string.IsNullOrWhiteSpace(model.CallId))
                throw new InvalidOperationException(
                    "Persisted function call does not contain a call ID.");

            if (string.IsNullOrWhiteSpace(model.ToolName))
                throw new InvalidOperationException(
                    "Persisted function call does not contain a tool name.");

            return new FunctionCallContent(
                model.CallId,
                model.ToolName,
                model.Arguments);
        }
        public string SerializeFunctionCall(
           string toolName,
           string callId,
           Dictionary<string, object?> arguments)
        {
            var model = new ToolCallPersistenceModel
            {
                CallId = callId,
                ToolName = toolName,
                Arguments = arguments
                    ?? new Dictionary<string, object?>()
            };

            return JsonSerializer.Serialize(model);
        }
        public string SerializeFunctionCall(
        FunctionCallContent functionCall)
        {
            var model = new ToolCallPersistenceModel
            {
                CallId = functionCall.CallId,
                ToolName = functionCall.Name,
                Arguments = functionCall.Arguments?
                    .ToDictionary(
                        x => x.Key,
                        x => x.Value)
                    ?? new Dictionary<string, object?>()
            };

            return JsonSerializer.Serialize(model);
        }

        public string SerializeFunctionResult(
            FunctionResultContent functionResult)
        {
            var model = new ToolCallPersistenceModel
            {
                CallId = functionResult.CallId,
                Result = SerializeResult(functionResult.Result)
            };

            return JsonSerializer.Serialize(model);
        }

        private static string? SerializeResult(object? result)
        {
            if (result == null)
                return null;

            if (result is string text)
                return text;

            return JsonSerializer.Serialize(result);
        }

    }
}
