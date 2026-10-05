namespace IronForge.Api.Helpers
{
    public class ApiErrorResponse
    {
        public int Status { get; set; }

        public string? Message { get; set; } = "";

        public string? TraceId { get; set; }
        public Dictionary<string, string[]>? Errors { get; set; }
    }
}
