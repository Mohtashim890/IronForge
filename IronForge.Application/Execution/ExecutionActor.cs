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

    /// <summary>A registered access terminal acting without a user.</summary>
    public int? DeviceId { get; init; }
}