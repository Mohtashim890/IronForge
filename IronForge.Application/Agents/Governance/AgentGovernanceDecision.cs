namespace IronForge.Application.Agents.Governance
{
    public enum AgentGovernanceDecisionType
    {
        Allow,
        ApprovalRequired,
        Deny
    }
    public class AgentGovernanceDecision
    {
        public AgentGovernanceDecisionType Decision { get; set; }

        public string? Reason { get; set; }

        public AgentRiskLevel RiskLevel { get; set; }

        public bool RequiresApproval { get; set; }
    }
    public enum AgentGovernanceExecutionMode
    {
        Normal,
        ApprovedExecution
    }
}
