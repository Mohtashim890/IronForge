using System.ComponentModel.DataAnnotations;

namespace IronForge.Shared.Models.Delegations;

public sealed class CreateAgentDelegationRequest
{
    [Required]
    public string AgentId { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string ClientId { get; set; } = string.Empty;

    [Required]
    [MinLength(1)]
    public List<string> Scopes { get; set; } = [];

    [Range(1, 60)]
    public int LifetimeMinutes { get; set; } = 60;
}
