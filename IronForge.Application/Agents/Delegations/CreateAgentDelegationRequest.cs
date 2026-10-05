using System.ComponentModel.DataAnnotations;

namespace IronForge.Application.Agents.Delegations;

public class CreateAgentDelegationRequest
{
    [Required]
    public string AgentId { get; set; } = "";

    [Required]
    [StringLength(200)]
    public string ClientId { get; set; } = "";

    [Required]
    [MinLength(1)]
    public List<string> Scopes { get; set; } = [];

    [Range(1, 60)]
    public int LifetimeMinutes { get; set; } = 60;
}