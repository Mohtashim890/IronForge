using System.Diagnostics;

namespace IronForge.Application.Observablity.Tracing
{
    public static class IronForgeToolTracing
    {
        public const string InvokeActivity =
            "IronForge.Tool.Invoke";

        public const string ExecuteActivity =
            "IronForge.Tool.Execute";

        public static void SetToolIdentity(
            Activity activity,
            string? toolName,
            string? toolCallId = null)
        {
            if (!string.IsNullOrWhiteSpace(toolName))
            {
                activity.SetTag(
                    "ironforge.tool.name",
                    toolName);
            }

            if (!string.IsNullOrWhiteSpace(toolCallId))
            {
                activity.SetTag(
                    "ironforge.tool.call_id",
                    toolCallId);
            }
        }
    }
}
