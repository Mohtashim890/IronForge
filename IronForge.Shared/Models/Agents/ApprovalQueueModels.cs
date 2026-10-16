namespace IronForge.Shared.Models.Agents
{
    /// <summary>High/Critical-risk agent actions that need a second approver.</summary>
    public sealed class ApprovalQueueDto
    {
        /// <summary>Requests by others the current user may approve or reject.</summary>
        public List<ApprovalQueueItemDto> AwaitingYourDecision { get; set; } = [];

        /// <summary>The current user's own requests, pending or ready to run.</summary>
        public List<ApprovalQueueItemDto> YourRequests { get; set; } = [];
    }

    public sealed class ApprovalQueueItemDto
    {
        public string ApprovalId { get; set; } = "";

        public string AgentId { get; set; } = "";

        public string AgentName { get; set; } = "";

        public string ToolName { get; set; } = "";

        public string ArgumentsJson { get; set; } = "";

        public string RiskLevel { get; set; } = "";

        /// <summary>Pending or Approved.</summary>
        public string Status { get; set; } = "";

        public string RequestedBy { get; set; } = "";

        public string? DecidedBy { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime ExpiresAtUtc { get; set; }

        /// <summary>The requester can run it now: another approver approved it.</summary>
        public bool CanRun { get; set; }
    }

    public sealed class ApprovalQueueDecisionRequest
    {
        public bool Approved { get; set; }

        public string? Reason { get; set; }
    }
}
