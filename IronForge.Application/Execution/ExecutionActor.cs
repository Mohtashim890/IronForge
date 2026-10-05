using IronForge.Application.Agents.Models;

namespace IronForge.Application.Execution;

public class ExecutionActor
{
    public int? UserId { get; init; }

    public string? Username { get; init; }
    public string? Role { get; init; }

    public IReadOnlySet<string> UserPermissions { get; init; }
       = new HashSet<string>();

    public AgentIdentity? Agent { get; set; }

    public string? ClientId { get; init; }
    public Guid? DelegationId { get; set; }
}