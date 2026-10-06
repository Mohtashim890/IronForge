using IronForge.Application.Agents.Delegations;

namespace IronForge.Application.Tests.Agents.Delegations;

public static class DelegationTestData
{
    public static AgentDelegation Create(
        Guid? delegationId = null,
        int userId = 42,
        string agentId = "product-agent",
        string clientId = "mcp-client",
        IEnumerable<string>? scopes = null,
        TimeSpan? remainingLifetime = null,
        bool revoked = false)
    {
        var now = DateTime.UtcNow;

        return new AgentDelegation
        {
            DelegationId =
                delegationId ?? Guid.NewGuid(),

            UserId = userId,

            AgentId = agentId,

            ClientId = clientId,

            Scopes =
                (scopes ??
                    new[]
                    {
                        "product.read",
                        "product.write"
                    })
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase),

            CreatedAtUtc =
                now.AddMinutes(-10),

            ExpiresAtUtc =
                now.Add(
                    remainingLifetime ??
                    TimeSpan.FromMinutes(30)),

            Revoked = revoked
        };
    }
}