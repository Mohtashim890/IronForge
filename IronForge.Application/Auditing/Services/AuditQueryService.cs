using IronForge.Application.Auditing.DTOs;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.Gym.Common;
using IronForge.Shared.Authorization;
using IronForge.Application.Persistence;
using IronForge.Shared.Models;

namespace IronForge.Application.Auditing.Services
{
    public sealed class AuditQueryService : IAuditQueryService
    {
        private readonly IRepository<AuditEvent> _auditEvents;
        private readonly IExecutionContextAccessor _executionContext;
        private readonly IGymAccessService _access;
        private readonly IRepository<User> _users;

        public AuditQueryService(
            IRepository<AuditEvent> auditEvents,
            IExecutionContextAccessor executionContext,
            IGymAccessService access,
            IRepository<User> users)
        {
            _users = users;
            _auditEvents = auditEvents;
            _executionContext = executionContext;
            _access = access;
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

            // The activity log names members and staff: admins (and agents acting for them) only.
            var authorization = await _access.AuthorizeAsync(Permissions.AuditRead, cancellationToken);

            if (!authorization.Allowed)
            {
                return ServiceResult<AuditQueryResult>.Fail(
                    authorization.Reason ?? "You do not have permission to read the activity log.",
                    403);
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

            var tenantId = context.TenantId.Value;

            // Filtered, ordered and paged in the database: the log grows with
            // every check-in, payment and sign-in.
            IQueryable<AuditEvent> Filter(IQueryable<AuditEvent> q)
            {
                q = q.Where(x => x.TenantId == tenantId);

                if (request.Category.HasValue)
                    q = q.Where(x => x.Category == request.Category.Value);
                if (request.Outcome.HasValue)
                    q = q.Where(x => x.Outcome == request.Outcome.Value);
                if (request.ActorType.HasValue)
                    q = q.Where(x => x.ActorType == request.ActorType.Value);
                if (request.UserId.HasValue)
                    q = q.Where(x => x.UserId == request.UserId.Value);
                if (!string.IsNullOrWhiteSpace(request.AgentId))
                    q = q.Where(x => x.AgentId == request.AgentId);
                if (!string.IsNullOrWhiteSpace(request.ResourceType))
                    q = q.Where(x => x.ResourceType == request.ResourceType);
                if (!string.IsNullOrWhiteSpace(request.ResourceId))
                    q = q.Where(x => x.ResourceId == request.ResourceId);
                if (!string.IsNullOrWhiteSpace(request.Action))
                    q = q.Where(x => x.Action == request.Action);
                if (!string.IsNullOrWhiteSpace(request.CorrelationId))
                    q = q.Where(x => x.CorrelationId == request.CorrelationId);
                if (!string.IsNullOrWhiteSpace(request.TraceId))
                    q = q.Where(x => x.TraceId == request.TraceId);
                if (request.FromUtc.HasValue)
                    q = q.Where(x => x.OccurredAtUtc >= request.FromUtc.Value);
                if (request.ToUtc.HasValue)
                    q = q.Where(x => x.OccurredAtUtc < request.ToUtc.Value);

                return q;
            }

            var totalCount = await _auditEvents.QueryCountAsync(Filter, cancellationToken);

            var skip = (request.Page - 1) * request.PageSize;

            var items = await _auditEvents.QueryAsync(
                q => Filter(q)
                    .OrderByDescending(x => x.OccurredAtUtc)
                    .ThenByDescending(x => x.Id)
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
                        ResourceType = x.ResourceType,
                        ResourceId = x.ResourceId,
                        CorrelationId = x.CorrelationId,
                        TraceId = x.TraceId,
                        Reason = x.Reason
                    }),
                cancellationToken);

            var userIds = items.Where(x => x.UserId.HasValue).Select(x => x.UserId!.Value).Distinct().ToList();

            if (userIds.Count > 0)
            {
                var names = (await _users.QueryAsync(
                        q => q.Where(x => userIds.Contains(x.Id)).Select(x => new { x.Id, x.Username }),
                        cancellationToken))
                    .ToDictionary(x => x.Id, x => x.Username);

                foreach (var item in items)
                {
                    item.Username = item.UserId is { } id ? names.GetValueOrDefault(id) : null;
                }
            }

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
