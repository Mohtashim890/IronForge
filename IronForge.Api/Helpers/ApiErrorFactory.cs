namespace IronForge.Api.Helpers
{
    public class ApiErrorFactory
    {
        public static ApiErrorResponse Create(
        int status,
        string? message,
        string? traceId = null,
        Dictionary<string, string[]>? errors = null)
        {
            return new ApiErrorResponse
            {
                Status = status,
                Message = message,
                TraceId = traceId,
                Errors = errors
            };
        }
    }
}
