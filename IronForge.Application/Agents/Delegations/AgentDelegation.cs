namespace IronForge.Application.Agents.Delegations;

public class AgentDelegation
{
    public Guid DelegationId { get; set; }

    public int UserId { get; set; }

    public string AgentId { get; set; } = "";
    public string ClientId { get; set; } = "";

    public HashSet<string> Scopes { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public bool Revoked { get; set; }

    public void Revoke()
    {
        Revoked = true;
    }
}