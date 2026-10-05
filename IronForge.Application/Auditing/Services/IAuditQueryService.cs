using IronForge.Application.Auditing.DTOs;
using IronForge.Shared.Models;

namespace IronForge.Application.Auditing.Services
{
    public interface IAuditQueryService
    {
        Task<ServiceResult<AuditQueryResult>> QueryAsync(
            AuditQueryRequest request,
            CancellationToken cancellationToken = default);
    }
}
