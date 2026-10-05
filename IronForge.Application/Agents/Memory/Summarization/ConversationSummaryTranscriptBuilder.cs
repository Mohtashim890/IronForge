using IronForge.Application.Agents.Memory.ConversationSession;
using IronForge.Application.Entities;
using System.Text;

namespace IronForge.Application.Agents.Memory.Summarization
{
    public class ConversationSummaryTranscriptBuilder
     : IConversationSummaryTranscriptBuilder
    {
        public string Build(
            IReadOnlyList<ConversationMessage> messages)
        {
            var builder = new StringBuilder();

            foreach (var message in messages)
            {
                switch (message.Role)
                {
                    case ConversationMessageRole.User:

                        builder.AppendLine("User:");
                        builder.AppendLine(message.Content);
                        builder.AppendLine();

                        break;

                    case ConversationMessageRole.Assistant:

                        if (!string.IsNullOrWhiteSpace(
                            message.ToolCallId))
                        {
                            builder.AppendLine("Assistant:");
                            builder.AppendLine(
                                $"Called tool: {message.ToolName}");

                            builder.AppendLine(
                                message.Content);

                            builder.AppendLine();
                        }
                        else
                        {
                            builder.AppendLine("Assistant:");
                            builder.AppendLine(message.Content);
                            builder.AppendLine();
                        }

                        break;

                    case ConversationMessageRole.Tool:

                        builder.AppendLine("Tool result:");
                        builder.AppendLine(message.Content);
                        builder.AppendLine();

                        break;
                }
            }

            return builder.ToString();
        }
    }
}
