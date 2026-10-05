namespace IronForge.Application.Agents.Governance
{
    public class AgentGovernanceContext
    {
        public string ToolName { get; set; } = "";

        public IDictionary<string, object?> Arguments { get; set; }
            = new Dictionary<string, object?>();
    }
}
