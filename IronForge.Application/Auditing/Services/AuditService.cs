using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.Persistence;

namespace IronForge.Application.Auditing.Services
{
    public class AuditService : IAuditService
    {
        private readonly IRepository<AuditEvent> _auditEvents;
        private readonly IExecutionContextAccessor _executionContext;

        public AuditService(
            IRepository<AuditEvent> auditEvents,
            IExecutionContextAccessor executionContext)
        {
            _auditEvents = auditEvents;
            _executionContext = executionContext;
        }

        public async Task RecordAsync(
            AuditCategory category,
            string action,
            AuditOutcome outcome,
            AuditActorType? actorType = null,
            string? resourceType = null,
            string? resourceId = null,
            string? reason = null,
            string? metadataJson = null,
            CancellationToken cancellationToken = default)
        {
            var context = _executionContext.Current;

            var actor = context?.Actor;

            var resolvedActorType =
                actorType
                ?? ResolveActorType(actor);

            var auditEvent = new AuditEvent
            {
                TenantId = context?.TenantId,

                OccurredAtUtc = DateTime.UtcNow,

                Category = category,
                Action = action,
                Outcome = outcome,
                ActorType = resolvedActorType,

                UserId = actor?.UserId,
                AgentId = actor?.Agent?.AgentId,
                ClientId = actor?.ClientId,
                DelegationId = actor?.DelegationId,

                ResourceType = resourceType,
                ResourceId = resourceId,

                CorrelationId = context?.CorrelationId,
                TraceId = context?.TraceId,

                Reason = reason,
                MetadataJson = metadataJson
            };

            _auditEvents.Add(auditEvent);

            await _auditEvents.SaveChangesAsync(cancellationToken);
        }

        private static AuditActorType ResolveActorType(
            ExecutionActor? actor)
        {
            if (actor?.Agent != null)
            {
                return AuditActorType.Agent;
            }

            if (actor?.UserId != null)
            {
                return AuditActorType.User;
            }

            return AuditActorType.System;
        }
    }
}
