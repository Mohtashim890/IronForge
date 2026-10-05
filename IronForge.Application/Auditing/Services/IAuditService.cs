using IronForge.Application.Auditing.Models;

namespace IronForge.Application.Auditing.Services
{
    public interface IAuditService
    {
        Task RecordAsync(
       AuditCategory category,
       string action,
       AuditOutcome outcome,
       AuditActorType? actorType = null,
       string? resourceType = null,
       string? resourceId = null,
       string? reason = null,
       string? metadataJson = null,
       CancellationToken cancellationToken = default);
    }
}
