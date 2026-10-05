using IronForge.Application.Agents.Memory.ConversationSession;
using IronForge.Application.Agents.Memory.Summarization;
using IronForge.Application.Agents.Memory.Tools;
using IronForge.Application.Configurations;
using IronForge.Application.Entities;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace IronForge.Application.Agents.Memory.Context
{
    public class ConversationContextService
    : IConversationContextService
    {
        private readonly IConversationService _conversationService;
        private readonly ConversationMessageMapper _messageMapper;
        private readonly ConversationContextOptions _options;
        private readonly IConversationTokenEstimator _tokenEstimator;
        private readonly IConversationContextCompactor _compactor;
        private readonly IConversationSummaryService _summaryService;
        private readonly IConversationContextUnitBuilder _unitBuilder;   
        public ConversationContextService(
            IConversationService conversationService,
            ConversationMessageMapper messageMapper,
            IOptions<ConversationContextOptions> options,
            IConversationTokenEstimator tokenEstimator,
            IConversationContextCompactor compactor,
            IConversationSummaryService summaryService,
            IConversationContextUnitBuilder unitBuilder)
        {
            _conversationService = conversationService;
            _messageMapper = messageMapper;
            _options = options.Value;
            _tokenEstimator = tokenEstimator;
            _compactor = compactor;
            _summaryService = summaryService;
            _unitBuilder = unitBuilder;
        }

        public async Task<IReadOnlyList<ChatMessage>> BuildContextAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
        {
            var result =
               new List<ChatMessage>();

            var summaryTokens = 0;

            var summary =
                _options.UseConversationSummary
                    ? await _summaryService.GetAsync(
                        sessionId,
                        cancellationToken)
                    : null;

            var history =
                await _conversationService.GetMessagesAsync(
                    sessionId,
                    cancellationToken);

            if (summary != null &&
               !string.IsNullOrWhiteSpace(summary.Content))
            {
                var summaryMessage =
                    BuildSummaryMessage(summary);

                result.Add(summaryMessage);
                summaryTokens =
                _tokenEstimator.EstimateMessageTokens(
                    summaryMessage);
            }

            var detailedHistory =
                GetDetailedHistory(
                    history,
                    summary);

            var selectedMessages =
                SelectRecentHistory(
                    detailedHistory, summaryTokens);

            result.AddRange(selectedMessages);

            return result;
        }

        private ChatMessage BuildSummaryMessage(ConversationSummary summary)
        {
            if (summary is null)
            {
                return null;
            }
            var content =
                $"""
        The following is a derived summary of older conversation
        history. Use it as background context. More recent
        conversation messages take precedence if they conflict
        with this summary.

        --- BEGIN CONVERSATION SUMMARY ---

        {summary.Content}

        --- END CONVERSATION SUMMARY ---
        """;

            return new ChatMessage(
                ChatRole.System,
                content);
        }

        private IReadOnlyList<ConversationMessage> GetDetailedHistory(
        IReadOnlyList<ConversationMessage> history,
        ConversationSummary? summary)
        {
            if (summary == null)
            {
                return history;
            }

            var units =
                _unitBuilder.BuildUnits(history);

            var detailed =
                new List<ConversationMessage>();

            foreach (var unit in units)
            {
                var lastMessage =
                    unit[^1];

                if (lastMessage.Id <=
                    summary.SummarizedThroughMessageId)
                {
                    continue;
                }

                detailed.AddRange(unit);
            }

            return detailed;
        }

        private List<ChatMessage> SelectRecentHistory(IReadOnlyList<ConversationMessage> history, int summaryTokens)
        {
            var units = BuildContextUnits(history);

            var historyBudget =
                CalculateHistoryBudget(summaryTokens);

            var selected =
                new List<ChatMessage>();

            var usedTokens = 0;
            var messageCount = 0;

            for (var i = units.Count - 1; i >= 0; i--)
            {
                var unit = units[i];

                var unitChatMessages = 
                    unit
                     .Select(_messageMapper.ToChatMessage)
                     .ToList();

                var compactedMessages =
                    unitChatMessages
                        .Select(_compactor.CompactToolResult)
                        .ToList();

                var unitTokens =
                    compactedMessages.Sum(
                        _tokenEstimator.EstimateMessageTokens);

                if (unitTokens > historyBudget)
                {
                    // The individual unit itself is too large.
                    // Do not split it. Handle with RAG and summarization
                    // how to handle it if unit lies in detailed history range?
                    continue;
                }

                if (usedTokens + unitTokens > historyBudget)
                {
                    break;
                }

                if (messageCount + unit.Count > _options.MaximumHistoryMessages)
                {
                    break;
                }

                selected.InsertRange(
                    0,
                    compactedMessages);

                usedTokens += unitTokens;
                messageCount += unit.Count;
            }

            return selected;
        }

        private int CalculateHistoryBudget(int summaryTokens = 0)
        {
            var budget =
                _options.MaximumContextTokens
                - _options.SystemPromptReserveTokens
                - _options.ToolDefinitionReserveTokens
                - _options.CurrentMessageReserveTokens
                - _options.ResponseReserveTokens
                - summaryTokens;

            if (budget <= 0)
            {
                throw new InvalidOperationException(
                    "Conversation context configuration leaves no room " +
                    "for conversation history.");
            }

            return budget;
        }
        private static List<List<ConversationMessage>> BuildContextUnits(IReadOnlyList<ConversationMessage> history)
        {
            var units = new List<List<ConversationMessage>>();
            if(history == null || history.Count == 0)
            {
                return units;
            }

            for (var i = 0; i < history.Count; i++)
            {
                var message = history[i];

                if (message.Role == ConversationMessageRole.Assistant &&
                    !string.IsNullOrWhiteSpace(message.ToolCallId))
                {
                    var unit = new List<ConversationMessage>
                    {
                        message
                    };

                    // Function result immediately following the call.
                    if (i + 1 < history.Count &&
                        history[i + 1].Role == ConversationMessageRole.Tool &&
                        history[i + 1].ToolCallId == message.ToolCallId)
                    {
                        unit.Add(history[i + 1]);
                        i++;
                    }

                    // Final assistant response following the tool result.
                    if (i + 1 < history.Count &&
                        history[i + 1].Role == ConversationMessageRole.Assistant &&
                        string.IsNullOrWhiteSpace(history[i + 1].ToolCallId))
                    {
                        unit.Add(history[i + 1]);
                        i++;
                    }

                    units.Add(unit);
                    continue;
                }

                units.Add(new List<ConversationMessage>
                {
                    message
                });
            }

            return units;
        }

    }
}
