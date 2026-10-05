﻿using IronForge.Application.Execution;
using IronForge.Application.Persistence;

namespace IronForge.Application.Agents.Memory.AgentMemory
{
    public class MemoryService : IMemoryService
    {
        private readonly IRepository<Entities.AgentMemory> _memoryRepository;
        private readonly IExecutionContextAccessor _executionContext;
        private readonly IMemoryAuthorizationService _memoryAuthorization;

        public MemoryService(
            IRepository<Entities.AgentMemory> memoryRepository,
            IExecutionContextAccessor executionContext,
            IMemoryAuthorizationService memoryAuthorization)
        {
            _memoryRepository = memoryRepository;
            _executionContext = executionContext;
            _memoryAuthorization = memoryAuthorization;
        }
        public async Task<Entities.AgentMemory?> GetAsync(long memoryId, CancellationToken cancellationToken = default)
        {
            GetExecutionContext();

            var memory = await _memoryRepository.FirstOrDefaultAsync(
                    x => x.Id == memoryId,
                    cancellationToken);

            if (memory == null)
                return null;

            var authorization =
                await _memoryAuthorization.AuthorizeReadAsync(
                    memory,
                    cancellationToken);

            if (!authorization.Allowed)
                return null;

            return memory;
        }

        public async Task<IReadOnlyList<Entities.AgentMemory>> GetTenantMemoriesAsync(
        CancellationToken cancellationToken = default)
        {
            var authorization =
               await _memoryAuthorization.AuthorizeCollectionReadAsync(MemoryScope.Tenant, cancellationToken);

            if (!authorization.Allowed)
                return null;

            var context = GetExecutionContext();

            if (!context.TenantId.HasValue)
            {
                throw new InvalidOperationException(
                    "Tenant context is required.");
            }

            return (await _memoryRepository.ListAsync(
                    x => x.Scope == MemoryScope.Tenant,
                    cancellationToken))
                .OrderByDescending(x => x.UpdatedAtUtc)
                .ToList();
        }

        public async Task<IReadOnlyList<Entities.AgentMemory>> GetUserMemoriesAsync(
        CancellationToken cancellationToken = default)
        {
            var authorization =
              await _memoryAuthorization.AuthorizeCollectionReadAsync(MemoryScope.User, cancellationToken);

            if (!authorization.Allowed)
                return null;

            var context = GetExecutionContext();

            if (!context.Actor.UserId.HasValue)
            {
                throw new InvalidOperationException(
                    "Authenticated user context is required.");
            }

            return (await _memoryRepository.ListAsync(
                    x =>
                        x.Scope == MemoryScope.User &&
                        x.UserId == context.Actor.UserId.Value,
                    cancellationToken))
                .OrderByDescending(x => x.UpdatedAtUtc)
                .ToList();
        }

        public async Task<IReadOnlyList<Entities.AgentMemory>> GetSessionMemoriesAsync(Guid sessionId,
            CancellationToken cancellationToken = default)
        {
            var authorization =
              await _memoryAuthorization.AuthorizeCollectionReadAsync(MemoryScope.Session, cancellationToken);

            if (!authorization.Allowed)
                return null;

            var context = GetExecutionContext();

            if (!context.Actor.UserId.HasValue)
            {
                throw new InvalidOperationException(
                    "Authenticated user context is required.");
            }

            return (await _memoryRepository.ListAsync(
                    x =>
                        x.Scope == MemoryScope.Session &&
                        x.SessionId == sessionId &&
                        x.UserId == context.Actor.UserId.Value,
                    cancellationToken))
                .OrderBy(x => x.CreatedAtUtc)
                .ToList();
        }

        public async Task<Entities.AgentMemory> CreateAsync(
            MemoryScope scope,
            MemoryType type,
            string content,
            Guid? sessionId = null,
            DateTime? expiresAtUtc = null,
            CancellationToken cancellationToken = default)
        {

            var authorization = await _memoryAuthorization.AuthorizeCreateAsync(scope, cancellationToken);
            if (!authorization.Allowed)
            {
                throw new UnauthorizedAccessException(
                    authorization.Reason);
            }

            var context = GetExecutionContext();
            if (!context.TenantId.HasValue)
            {
                throw new InvalidOperationException(
                    "Tenant context is required to create memory.");
            }

            var userId = context.Actor.UserId;

            ValidateScope(
                scope,
                userId,
                sessionId);

            if (string.IsNullOrWhiteSpace(content))
            {
                throw new ArgumentException(
                    "Memory content cannot be empty.",
                    nameof(content));
            }

            var memory = new Entities.AgentMemory
            {
                TenantId = context.TenantId.Value,

                UserId = scope == MemoryScope.User ||
                         scope == MemoryScope.Session
                    ? userId
                    : null,

                AgentId = context.Actor.Agent?.AgentId,

                SessionId = scope == MemoryScope.Session
                    ? sessionId
                    : null,

                Scope = scope,

                Type = type,

                Content = content.Trim(),

                Source = context.Actor.Agent != null
                    ? MemorySource.Agent
                    : MemorySource.User,

                CreatedAtUtc = DateTime.UtcNow,

                UpdatedAtUtc = DateTime.UtcNow,

                ExpiresAtUtc = expiresAtUtc
            };

            _memoryRepository.Add(memory);

            await _memoryRepository.SaveChangesAsync(cancellationToken);

            return memory;
        }

        public async Task DeleteAsync(long memoryId, CancellationToken cancellationToken = default)
        {
            GetExecutionContext();

            var memory = await _memoryRepository.FirstOrDefaultAsync(
                    x => x.Id == memoryId,
                    cancellationToken);

            if (memory == null)
            {
                throw new KeyNotFoundException(
                    "Memory was not found.");
            }

            var authorization = await _memoryAuthorization.AuthorizeDeleteAsync(memory, cancellationToken);

            if (!authorization.Allowed)
            {
                throw new UnauthorizedAccessException(
                    authorization.Reason);
            }

            _memoryRepository.Remove(memory);

            await _memoryRepository.SaveChangesAsync(cancellationToken);
        }
        private Execution.ExecutionContext GetExecutionContext()
        {
            return _executionContext.Current
                ?? throw new InvalidOperationException(
                    "Execution context is not established.");
        }

        private static void ValidateScope(
            MemoryScope scope,
            int? userId,
            Guid? sessionId)
        {
            switch (scope)
            {
                case MemoryScope.Tenant:

                    if (sessionId.HasValue)
                    {
                        throw new InvalidOperationException(
                            "Tenant memory cannot have a session.");
                    }

                    break;

                case MemoryScope.User:

                    if (!userId.HasValue)
                    {
                        throw new InvalidOperationException(
                            "User memory requires a user.");
                    }

                    if (sessionId.HasValue)
                    {
                        throw new InvalidOperationException(
                            "User memory cannot have a session.");
                    }

                    break;

                case MemoryScope.Session:

                    if (!userId.HasValue)
                    {
                        throw new InvalidOperationException(
                            "Session memory requires a user.");
                    }

                    if (!sessionId.HasValue)
                    {
                        throw new InvalidOperationException(
                            "Session memory requires a session.");
                    }

                    break;

                default:

                    throw new InvalidOperationException(
                        "Invalid memory scope.");
            }
        }
    }
}
