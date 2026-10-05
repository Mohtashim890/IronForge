namespace IronForge.Application.Agents.Memory.Tools
{
    public class ToolCallPersistenceModel
    {
        public string CallId { get; set; } = "";

        public string ToolName { get; set; } = "";

        public Dictionary<string, object?> Arguments { get; set; }
            = new();

        public string? Result { get; set; }

        public bool IsError { get; set; }
    }
}
