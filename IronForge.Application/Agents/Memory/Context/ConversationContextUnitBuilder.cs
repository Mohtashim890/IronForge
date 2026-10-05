using IronForge.Application.Agents.Memory.ConversationSession;
using IronForge.Application.Entities;

namespace IronForge.Application.Agents.Memory.Context
{
    public class ConversationContextUnitBuilder
     : IConversationContextUnitBuilder
    {
        public IReadOnlyList<IReadOnlyList<ConversationMessage>>
            BuildUnits(
                IReadOnlyList<ConversationMessage> history)
        {
            var units =
                new List<IReadOnlyList<ConversationMessage>>();

            if (history == null || history.Count == 0)
            {
                return units;
            }

            var i = 0;

            while (i < history.Count)
            {
                var message = history[i];

                // ---------------------------------------------------------
                // Normal message
                // ---------------------------------------------------------

                if (!IsToolCall(message) &&
                    !IsToolResult(message))
                {
                    units.Add(
                        new List<ConversationMessage>
                        {
                        message
                        });

                    i++;
                    continue;
                }

                // ---------------------------------------------------------
                // Orphan tool result
                // ---------------------------------------------------------

                if (IsToolResult(message))
                {
                    // Never expose an orphan tool result to the model.
                    i++;
                    continue;
                }

                // ---------------------------------------------------------
                // Tool call
                // ---------------------------------------------------------

                var toolCallId = message.ToolCallId!;

                var unit =
                    new List<ConversationMessage>
                    {
                    message
                    };

                var callIndex = i;

                i++;

                // ---------------------------------------------------------
                // Look only within the immediately following tool segment.
                // ---------------------------------------------------------

                var matchingResultIndex = -1;

                while (i < history.Count)
                {
                    var candidate = history[i];

                    // Another user message means this tool call was
                    // interrupted and never completed.
                    if (candidate.Role ==
                        ConversationMessageRole.User)
                    {
                        break;
                    }

                    // Another independent assistant response means the
                    // original tool call is incomplete.
                    if (candidate.Role ==
                            ConversationMessageRole.Assistant &&
                        !string.Equals(
                            candidate.ToolCallId,
                            toolCallId,
                            StringComparison.Ordinal))
                    {
                        break;
                    }

                    if (IsToolResult(candidate) &&
                        string.Equals(
                            candidate.ToolCallId,
                            toolCallId,
                            StringComparison.Ordinal))
                    {
                        matchingResultIndex = i;
                        break;
                    }

                    // Orphan/mismatched tool result.
                    if (IsToolResult(candidate))
                    {
                        break;
                    }

                    i++;
                }

                // ---------------------------------------------------------
                // No result = incomplete tool call.
                // ---------------------------------------------------------

                if (matchingResultIndex == -1)
                {
                    // We deliberately discard the call from LLM context.
                    //
                    // IMPORTANT:
                    // Do not rewind i. The following user/assistant message
                    // still needs to be processed normally.
                    i = callIndex + 1;

                    continue;
                }
                if (!IsMatchingToolResult(message, history[matchingResultIndex]))
                {
                    i = callIndex + 1;

                    continue;
                }

                // ---------------------------------------------------------
                // Complete tool interaction.
                // ---------------------------------------------------------

                unit.Add(history[matchingResultIndex]);

                i = matchingResultIndex + 1;

                // ---------------------------------------------------------
                // Optional final assistant response.
                // ---------------------------------------------------------

                if (i < history.Count)
                {
                    var candidate = history[i];

                    if (candidate.Role ==
                            ConversationMessageRole.Assistant &&
                        string.IsNullOrWhiteSpace(
                            candidate.ToolCallId))
                    {
                        unit.Add(candidate);
                        i++;
                    }
                }

                units.Add(unit);
            }

            return units;
        }
        private static bool IsMatchingToolResult(
            ConversationMessage toolCall,
            ConversationMessage toolResult)
        {
            return
                toolCall.Role ==
                    ConversationMessageRole.Assistant &&
                toolResult.Role ==
                    ConversationMessageRole.Tool &&
                !string.IsNullOrWhiteSpace(
                    toolCall.ToolCallId) &&
                string.Equals(
                    toolCall.ToolCallId,
                    toolResult.ToolCallId,
                    StringComparison.Ordinal);
        }
        private static bool IsToolCall(
            ConversationMessage message)
        {
            return
                message.Role ==
                    ConversationMessageRole.Assistant &&
                !string.IsNullOrWhiteSpace(
                    message.ToolCallId);
        }

        private static bool IsToolResult(
            ConversationMessage message)
        {
            return
                message.Role ==
                    ConversationMessageRole.Tool &&
                !string.IsNullOrWhiteSpace(
                    message.ToolCallId);
        }
    }
}
