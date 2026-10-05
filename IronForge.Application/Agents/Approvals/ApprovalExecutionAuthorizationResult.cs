namespace IronForge.Application.Agents.Approvals
{
    public sealed class ApprovalExecutionAuthorizationResult
    {
        public ApprovalExecutionDecision Decision { get; init; }

        public string Reason { get; init; } = "";

        public bool IsAllowed =>
            Decision == ApprovalExecutionDecision.Allow;
    }
    public enum ApprovalExecutionDecision
    {
        Allow,
        Deny
    }
}


