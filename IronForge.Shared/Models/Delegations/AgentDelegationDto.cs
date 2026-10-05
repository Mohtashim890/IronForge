namespace IronForge.Shared.Models.Delegations;

public sealed class AgentDelegationDto
{
    public Guid DelegationId { get; set; }

    public int UserId { get; set; }

    public string AgentId { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public List<string> Scopes { get; set; } = [];

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public bool Revoked { get; set; }

    public bool IsExpired =>
        ExpiresAtUtc <= DateTime.UtcNow;

    public bool IsActive =>
        !Revoked && !IsExpired;
}
