namespace IronForge.Application.Entities
{
    public class AgentDelegationScopeEntity
    {
        public Guid DelegationId { get; set; }

        public string Scope { get; set; } = "";

        public AgentDelegationEntity Delegation { get; set; } = null!;
    }
}
