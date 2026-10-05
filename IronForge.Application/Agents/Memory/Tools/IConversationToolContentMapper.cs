using Microsoft.Extensions.AI;

namespace IronForge.Application.Agents.Memory.Tools
{
    public interface IConversationToolContentMapper
    {
        string SerializeFunctionCall(
            FunctionCallContent functionCall);

        string SerializeFunctionCall(
            string toolName,
            string callId,
            Dictionary<string, object?> arguments);

        string SerializeFunctionResult(
            FunctionResultContent functionResult);

        FunctionCallContent DeserializeFunctionCall(
        string json);

        FunctionResultContent DeserializeFunctionResult(
            string json);
        AIFunctionArguments DeserializeFunctionArguments(
           AIFunction function,
           string argumentsJson);
    }
}
