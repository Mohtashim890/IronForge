namespace IronForge.Application.Entities
{
    /// <summary>
    /// A catalogue tool granted to an agent, with optional overrides that
    /// tighten the tool's default governance. Null columns mean "use the
    /// tool's default". Tenant isolation comes from the owning agent.
    /// </summary>
    public class AgentToolBindingEntity
    {
        public int Id { get; set; }

        public int AgentId { get; set; }

        public string ToolName { get; set; } = "";

        public bool? RequiresApproval { get; set; }

        public string? RiskLevel { get; set; }

        public decimal? MaximumAmount { get; set; }

        public Agent Agent { get; set; } = null!;
    }
}
