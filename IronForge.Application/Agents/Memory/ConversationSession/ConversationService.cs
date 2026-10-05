﻿using IronForge.Application.Entities;
using IronForge.Application.Persistence;
using IronForge.Application.Execution;

namespace IronForge.Application.Agents.Memory.ConversationSession
{
    public class ConversationService : IConversationService
    {
        private readonly IRepository<AgentSession> _sessionRepository;
        private readonly IRepository<ConversationMessage> _messageRepository;
        private readonly IExecutionContextAccessor _executionContext;
        private readonly IConversationAccessService _conversationAccess;

        public ConversationService(
            IRepository<AgentSession> sessionRepository,
            IRepository<ConversationMessage> messageRepository,
            IExecutionContextAccessor executionContext,
            IConversationAccessService conversationAccess)
        {
            _sessionRepository = sessionRepository;
            _messageRepository = messageRepository;
            _executionContext = executionContext;
            _conversationAccess = conversationAccess;
        }

        public async Task<AgentSession> CreateSessionAsync(
            string agentId,
            string? title = null,
            CancellationToken cancellationToken = default)
        {
            var context = GetExecutionContext();

            if (!context.TenantId.HasValue)
            {
                throw new InvalidOperationException(
                    "Tenant context is required.");
            }

            if (!context.Actor.UserId.HasValue)
            {
                throw new InvalidOperationException(
                    "Authenticated user context is required.");
            }

            if (string.IsNullOrWhiteSpace(agentId))
            {
                throw new ArgumentException(
                    "Agent ID is required.",
                    nameof(agentId));
            }

            if (context.Actor.Agent != null && !string.Equals(
                context.Actor.Agent.AgentId,
                agentId.Trim(),
                StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException(
                    "Agent cannot create a session for another agent.");
            }

            var now = DateTime.UtcNow;

            var session = new AgentSession
            {
                Id = Guid.NewGuid(),

                TenantId = context.TenantId.Value,

                UserId = context.Actor.UserId.Value,

                AgentId = agentId.Trim(),

                Title = string.IsNullOrWhiteSpace(title)
                    ? null
                    : title.Trim(),

                Status = AgentSessionStatus.Active,

                CreatedAtUtc = now,

                UpdatedAtUtc = now,

                LastMessageAtUtc = null
            };

            _sessionRepository.Add(session);

            await _sessionRepository.SaveChangesAsync(cancellationToken);

            return session;
        }

        public async Task<AgentSession?> GetSessionAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default)
        {
            var context = GetExecutionContext();

            if (!context.Actor.UserId.HasValue)
            {
                throw new InvalidOperationException(
                    "Authenticated user context is required.");
            }

            var session = await _sessionRepository.FirstOrDefaultAsync(
                    x => x.Id == sessionId,
                    cancellationToken);

            if (session == null || await GetAuthorizedSessionAsync(session.Id, cancellationToken) == null)
            {
                return null;
            }

            return session;
        }

        public async Task<IReadOnlyList<AgentSession>> GetMySessionsAsync(
        string? agentId = null,
        CancellationToken cancellationToken = default)
        {
            var context = GetExecutionContext();

            if (!context.Actor.UserId.HasValue)
            {
                throw new InvalidOperationException(
                    "Authenticated user context is required.");
            }

            var userId = context.Actor.UserId.Value;

            var sessions = await _sessionRepository.ListAsync(
                x => x.UserId == userId,
                cancellationToken);

            if (context.Actor.Agent != null)
            {
                var currentAgentId =
                    context.Actor.Agent.AgentId;

                sessions = sessions.Where(
                    x => x.AgentId == currentAgentId).ToList();
            }
            else if (!string.IsNullOrWhiteSpace(agentId))
            {
                var normalizedAgentId = agentId.Trim();

                sessions = sessions.Where(
                    x => x.AgentId == normalizedAgentId).ToList();
            }

            return sessions
                .OrderByDescending(x => x.LastMessageAtUtc)
                .ThenByDescending(x => x.UpdatedAtUtc)
                .ToList();
        }

        public async Task<ConversationMessage> AddMessageAsync(
            Guid sessionId,
            ConversationMessageRole role,
            string content,
            string? toolCallId = null,
            string? toolName = null,
            string? metadataJson = null,
            CancellationToken cancellationToken = default)
        {
            var context = GetExecutionContext();

            if (!context.Actor.UserId.HasValue)
            {
                throw new InvalidOperationException(
                    "Authenticated user context is required.");
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                throw new ArgumentException(
                    "Message content cannot be empty.",
                    nameof(content));
            }

            var session = await _sessionRepository.FirstOrDefaultTrackedAsync(
                    x => x.Id == sessionId,
                    cancellationToken);

            if (session == null)
            {
                throw new KeyNotFoundException(
                    "Conversation session was not found.");
            }

            if (await GetAuthorizedSessionAsync(session.Id, cancellationToken) == null)
            {
                throw new KeyNotFoundException(
                    "Conversation session was not found.");
            }

            if (session.Status != AgentSessionStatus.Active)
            {
                throw new InvalidOperationException(
                    "Messages cannot be added to an archived session.");
            }

            var message = new ConversationMessage
            {
                SessionId = session.Id,

                TenantId = session.TenantId,

                Role = role,

                Content = content.Trim(),

                ToolCallId = toolCallId,

                ToolName = toolName,

                MetadataJson = metadataJson,

                CreatedAtUtc = DateTime.UtcNow
            };

            _messageRepository.Add(message);

            session.LastMessageAtUtc = message.CreatedAtUtc;
            session.UpdatedAtUtc = message.CreatedAtUtc;

            // _sessionRepository.Update(session);
            await _sessionRepository.SaveChangesAsync(cancellationToken);

            return message;
        }

        public async Task<IReadOnlyList<ConversationMessage>?> GetMessagesAsync(
         Guid sessionId,
         CancellationToken cancellationToken = default)
        {
            var context = GetExecutionContext();

            if (!context.Actor.UserId.HasValue)
            {
                throw new InvalidOperationException(
                    "Authenticated user context is required.");
            }

            var session = await _sessionRepository.FirstOrDefaultAsync(
                    x => x.Id == sessionId,
                    cancellationToken);

            if (session == null)
            {
                return null;
            }

            if (await GetAuthorizedSessionAsync(
                    session.Id,
                    cancellationToken) == null)
            {
                return null;
            }

            return (await _messageRepository.ListAsync(
                    x => x.SessionId == sessionId,
                    cancellationToken))
                .OrderBy(x => x.CreatedAtUtc)
                .ThenBy(x => x.Id)
                .ToList();
        }

        public async Task ArchiveSessionAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default)
        {
            var context = GetExecutionContext();

            if (!context.Actor.UserId.HasValue)
            {
                throw new InvalidOperationException(
                    "Authenticated user context is required.");
            }

            var session = await _sessionRepository.FirstOrDefaultTrackedAsync(
                    x => x.Id == sessionId,
                    cancellationToken);

            if (session == null ||
                session.UserId != context.Actor.UserId.Value)
            {
                throw new KeyNotFoundException(
                    "Conversation session was not found.");
            }

            if (session.Status == AgentSessionStatus.Archived)
            {
                return;
            }

            if (await GetAuthorizedSessionAsync(session.Id, cancellationToken) == null)
            {
                throw new KeyNotFoundException(
                    "Conversation session was not found.");
            }

            session.Status = AgentSessionStatus.Archived;
            session.UpdatedAtUtc = DateTime.UtcNow;

            //_sessionRepository.Update(session);
            await _sessionRepository.SaveChangesAsync(cancellationToken);
        }
        private async Task<AgentSession?> GetAuthorizedSessionAsync(
            Guid sessionId,
            CancellationToken cancellationToken)
        {
            var session = await _sessionRepository.FirstOrDefaultAsync(
                    x => x.Id == sessionId,
                    cancellationToken);

            if (session == null)
                return null;

            var access =
                _conversationAccess.AuthorizeSession(session);

            if (!access.Allowed)
                return null;

            return session;
        }


        private Execution.ExecutionContext GetExecutionContext()
        {
            return _executionContext.Current
                ?? throw new InvalidOperationException(
                    "Execution context is not established.");
        }
    }
}