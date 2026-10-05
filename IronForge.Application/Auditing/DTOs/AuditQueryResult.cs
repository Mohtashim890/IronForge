namespace IronForge.Application.Auditing.DTOs
{
    public sealed class AuditQueryResult
    {
        public IReadOnlyList<AuditEventDto> Items { get; init; }
            = Array.Empty<AuditEventDto>();

        public int TotalCount { get; init; }

        public int Page { get; init; }

        public int PageSize { get; init; }
    }
}
