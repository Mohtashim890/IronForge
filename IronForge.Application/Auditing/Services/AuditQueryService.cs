using IronForge.Application.Auditing.DTOs;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.Persistence;
using IronForge.Shared.Models;

namespace IronForge.Application.Auditing.Services
{
    public sealed class AuditQueryService : IAuditQueryService
    {
        private readonly IRepository<AuditEvent> _auditEvents;
        private readonly IExecutionContextAccessor _executionContext;

        public AuditQueryService(
            IRepository<AuditEvent> auditEvents,
            IExecutionContextAccessor executionContext)
        {
            _auditEvents = auditEvents;
            _executionContext = executionContext;
        }

        public async Task<ServiceResult<AuditQueryResult>> QueryAsync(
            AuditQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            var context = _executionContext.Current;

            if (context == null)
            {
                return ServiceResult<AuditQueryResult>.Fail(
                    "Execution context is unavailable.");
            }

            if (context.TenantId == null)
            {
                return ServiceResult<AuditQueryResult>.Fail(
                    "A tenant context is required to query audit events.");
            }

            if (request.Page < 1)
            {
                return ServiceResult<AuditQueryResult>.Fail(
                    "Page must be greater than or equal to 1.");
            }

            if (request.PageSize < 1 || request.PageSize > 100)
            {
                return ServiceResult<AuditQueryResult>.Fail(
                    "PageSize must be between 1 and 100.");
            }

            if (request.FromUtc.HasValue &&
                request.ToUtc.HasValue &&
                request.FromUtc >= request.ToUtc)
            {
                return ServiceResult<AuditQueryResult>.Fail(
                    "FromUtc must be earlier than ToUtc.");
            }

            // IRepository intentionally does not expose IQueryable.
            // Retrieve the tenant-scoped data and apply the existing
            // filtering, ordering, paging, and projection in memory.
            var query = await _auditEvents.ListAsync(
                x => x.TenantId == context.TenantId,
                cancellationToken);

            if (request.Category.HasValue)
            {
                query = query
                    .Where(x =>
                        x.Category == request.Category.Value)
                    .ToList();
            }

            if (request.Outcome.HasValue)
            {
                query = query
                    .Where(x =>
                        x.Outcome == request.Outcome.Value)
                    .ToList();
            }

            if (request.ActorType.HasValue)
            {
                query = query
                    .Where(x =>
                        x.ActorType == request.ActorType.Value)
                    .ToList();
            }

            if (request.UserId.HasValue)
            {
                query = query
                    .Where(x =>
                        x.UserId == request.UserId.Value)
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(request.AgentId))
            {
                query = query
                    .Where(x =>
                        x.AgentId == request.AgentId)
                    .ToList();
            }

            if (request.DelegationId.HasValue)
            {
                query = query
                    .Where(x =>
                        x.DelegationId == request.DelegationId.Value)
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(request.ResourceType))
            {
                query = query
                    .Where(x =>
                        x.ResourceType == request.ResourceType)
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(request.ResourceId))
            {
                query = query
                    .Where(x =>
                        x.ResourceId == request.ResourceId)
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(request.Action))
            {
                query = query
                    .Where(x =>
                        x.Action == request.Action)
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(request.CorrelationId))
            {
                query = query
                    .Where(x =>
                        x.CorrelationId == request.CorrelationId)
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(request.TraceId))
            {
                query = query
                    .Where(x =>
                        x.TraceId == request.TraceId)
                    .ToList();
            }

            if (request.FromUtc.HasValue)
            {
                query = query
                    .Where(x =>
                        x.OccurredAtUtc >= request.FromUtc.Value)
                    .ToList();
            }

            if (request.ToUtc.HasValue)
            {
                query = query
                    .Where(x =>
                        x.OccurredAtUtc < request.ToUtc.Value)
                    .ToList();
            }

            query = query
                .OrderByDescending(x => x.OccurredAtUtc)
                .ThenByDescending(x => x.Id)
                .ToList();

            var totalCount = query.Count;

            var skip = (request.Page - 1) * request.PageSize;

            var items = query
                .Skip(skip)
                .Take(request.PageSize)
                .Select(x => new AuditEventDto
                {
                    Id = x.Id,
                    OccurredAtUtc = x.OccurredAtUtc,
                    Category = x.Category,
                    Action = x.Action,
                    Outcome = x.Outcome,
                    ActorType = x.ActorType,
                    UserId = x.UserId,
                    AgentId = x.AgentId,
                    ClientId = x.ClientId,
                    DelegationId = x.DelegationId,
                    ResourceType = x.ResourceType,
                    ResourceId = x.ResourceId,
                    CorrelationId = x.CorrelationId,
                    TraceId = x.TraceId,
                    Reason = x.Reason
                })
                .ToList();

            var result = new AuditQueryResult
            {
                Items = items,
                TotalCount = totalCount,
                Page = request.Page,
                PageSize = request.PageSize
            };

            return ServiceResult<AuditQueryResult>.Ok(result);
        }
    }
}
