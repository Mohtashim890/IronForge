namespace IronForge.Application.Observablity.Tracing
{
    public static class ToolExecutionStatuses
    {
        public const string Requested =
            "requested";

        public const string Allowed =
            "allowed";

        public const string Denied =
            "denied";

        public const string ApprovalRequired =
            "approval_required";

        public const string AuthorizationDenied =
            "authorization_denied";

        public const string Executing =
            "executing";

        public const string Completed =
            "completed";

        public const string Failed =
            "failed";
    }
}
