namespace IronForge.Application.Agents.Governance
{
    public class AgentGovernanceContext
    {
        public string ToolName { get; set; } = "";

        public IDictionary<string, object?> Arguments { get; set; }
            = new Dictionary<string, object?>();

        /// <summary>
        /// Normal: the model is calling the tool directly.
        /// ApprovedExecution: a human has approved this call and it is being executed.
        /// </summary>
        public AgentGovernanceExecutionMode ExecutionMode { get; set; }
            = AgentGovernanceExecutionMode.Normal;
    }
}
