namespace IronForge.Application.Entities
{
    public class AgentDelegationEntity
    {
        public Guid DelegationId { get; set; }

        public int UserId { get; set; }

        public string AgentId { get; set; } = "";
        public string ClientId { get; set; } = "";

        public DateTime CreatedAtUtc { get; set; }

        public DateTime ExpiresAtUtc { get; set; }

        public bool Revoked { get; set; }

        public ICollection<AgentDelegationScopeEntity> Scopes { get; set; }
            = new List<AgentDelegationScopeEntity>();
    }
}
