namespace IronForge.Application.Agents.Governance
{
    public class AgentToolPolicy
    {
        public string ToolName { get; set; } = "";

        public string RequiredPermission { get; set; } = "";

        public AgentRiskLevel RiskLevel { get; set; }

        public bool RequiresApproval { get; set; }

        public decimal? MaximumAmount { get; set; }
    }
}
