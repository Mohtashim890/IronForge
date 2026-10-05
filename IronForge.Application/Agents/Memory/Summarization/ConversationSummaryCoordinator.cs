using IronForge.Application.Agents.Memory.Context;
using IronForge.Application.Agents.Memory.ConversationSession;
using IronForge.Application.Entities;

namespace IronForge.Application.Agents.Memory.Summarization
{
    public class ConversationSummaryCoordinator
     : IConversationSummaryCoordinator
    {
        private readonly IConversationService _conversationService;
        private readonly IConversationSummaryService _summaryService;
        private readonly IConversationSummaryPolicy _policy;
        private readonly IConversationTokenEstimator _tokenEstimator;

        public ConversationSummaryCoordinator(
            IConversationService conversationService,
            IConversationSummaryService summaryService,
            IConversationSummaryPolicy policy,
            IConversationTokenEstimator tokenEstimator)
        {
            _conversationService = conversationService;
            _summaryService = summaryService;
            _policy = policy;
            _tokenEstimator = tokenEstimator;
        }

        public async Task EvaluateAndSummarizeAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default)
        {
            var session =
                await _conversationService.GetSessionAsync(
                    sessionId,
                    cancellationToken);

            if (session == null)
            {
                return;
            }

            if (session.Status != AgentSessionStatus.Active)
            {
                return;
            }

            var summary =
                await _summaryService.GetAsync(
                    sessionId,
                    cancellationToken);

            var history =
                await _conversationService.GetMessagesAsync(
                    sessionId,
                    cancellationToken);

            var watermark =
                summary?.SummarizedThroughMessageId ?? 0;

            var unsummarizedMessages =
                history
                    .Where(x => x.Id > watermark)
                    .ToList();

            if (unsummarizedMessages.Count == 0)
            {
                return;
            }

            var estimatedTokens =
                EstimateTokens(unsummarizedMessages);

            var policyContext =
                new ConversationSummaryPolicyContext
                {
                    UnsummarizedMessageCount =
                        unsummarizedMessages.Count,

                    EstimatedUnsummarizedTokens =
                        estimatedTokens,

                    HasExistingSummary =
                        summary != null,

                    IsSessionActive =
                        session.Status ==
                        AgentSessionStatus.Active
                };

            if (!_policy.ShouldSummarize(
                policyContext))
            {
                return;
            }

            await _summaryService.CreateOrUpdateAsync(
                sessionId,
                cancellationToken);
        }

        private int EstimateTokens(
            IReadOnlyList<ConversationMessage> messages)
        {
            return messages.Sum(
                message =>
                    _tokenEstimator.EstimateTextTokens(
                        message.Content));
        }
    }
}
