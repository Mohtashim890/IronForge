﻿﻿using IronForge.Application.Agents.Memory.ConversationSession;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.Persistence;

namespace IronForge.Application.Agents.Memory.Summarization
{
    public class ConversationSummaryService
     : IConversationSummaryService
    {
        private readonly IRepository<ConversationSummary> _summaryRepository;
        private readonly IConversationService _conversationService;
        private readonly IAgentConversationSummarizer _summarizer;
        private readonly IExecutionContextAccessor _executionContext;

        public ConversationSummaryService(
            IRepository<ConversationSummary> summaryRepository,
            IConversationService conversationService,
            IAgentConversationSummarizer summarizer,
            IExecutionContextAccessor executionContext)
        {
            _summaryRepository = summaryRepository;
            _conversationService = conversationService;
            _summarizer = summarizer;
            _executionContext = executionContext;
        }

        public async Task<ConversationSummary?> GetAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default)
        {
            var context =
                _executionContext.Current
                ?? throw new InvalidOperationException(
                    "Execution context has not been established.");

            var summary =
                await GetSingleOrDefaultAsync(
                    x =>
                        x.SessionId == sessionId &&
                        x.TenantId == context.TenantId,
                    cancellationToken);

            return summary;
        }

        public async Task<ConversationSummary>
            CreateOrUpdateAsync(
                Guid sessionId,
                CancellationToken cancellationToken = default)
        {
            var context =
                _executionContext.Current
                ?? throw new InvalidOperationException(
                    "Execution context has not been established.");

            if (!context.TenantId.HasValue)
            {
                throw new InvalidOperationException(
                    "Tenant context is required.");
            }

            var session =
                await _conversationService.GetSessionAsync(
                    sessionId,
                    cancellationToken);

            if (session == null)
            {
                throw new InvalidOperationException(
                    "Conversation session was not found.");
            }

            if (session.TenantId != context.TenantId.Value)
            {
                throw new UnauthorizedAccessException(
                    "Conversation session does not belong to the current tenant.");
            }

            var existingSummary =
                await GetSingleOrDefaultAsync(
                    x => x.SessionId == sessionId,
                    cancellationToken);

            var summarizedThrough =
                existingSummary?.SummarizedThroughMessageId ?? 0;

            var messages =
                await _conversationService.GetMessagesAsync(
                    sessionId,
                    cancellationToken);

            var messagesToSummarize =
                messages
                    .Where(x =>
                        x.Id > summarizedThrough)
                    .OrderBy(x => x.Id)
                    .ToList();

            if (messagesToSummarize.Count == 0)
            {
                if (existingSummary != null)
                {
                    return existingSummary;
                }

                throw new InvalidOperationException(
                    "There are no conversation messages to summarize.");
            }

            var newSummary =
                await _summarizer.SummarizeAsync(
                    messagesToSummarize,
                    existingSummary?.Content,
                    cancellationToken);

            if (string.IsNullOrWhiteSpace(newSummary))
            {
                throw new InvalidOperationException(
                    "The conversation summarizer returned an empty summary.");
            }

            var lastMessageId =
                messagesToSummarize[^1].Id;

            if (existingSummary == null)
            {
                existingSummary = new ConversationSummary
                {
                    SessionId = sessionId,
                    TenantId = context.TenantId.Value,
                    SummarizedThroughMessageId = lastMessageId,
                    Content = newSummary,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };

                _summaryRepository.Add(existingSummary);
            }
            else
            {
                existingSummary.SummarizedThroughMessageId =
                    lastMessageId;

                existingSummary.Content =
                    newSummary;

                existingSummary.UpdatedAtUtc =
                    DateTime.UtcNow;

                //_summaryRepository.Update(existingSummary);
            }

            await _summaryRepository.SaveChangesAsync(
                cancellationToken);

            return existingSummary;
        }


        private async Task<ConversationSummary?> GetSingleOrDefaultAsync(
            System.Linq.Expressions.Expression<Func<ConversationSummary, bool>> predicate,
            CancellationToken cancellationToken)
        {
            var matches = await _summaryRepository.ListTrackedAsync(
                predicate,
                cancellationToken);

            return matches.SingleOrDefault();
        }
    }
}