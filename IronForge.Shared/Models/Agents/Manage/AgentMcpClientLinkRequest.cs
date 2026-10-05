using System.ComponentModel.DataAnnotations;

namespace IronForge.Shared.Models.Agents;

public sealed class AgentMcpClientLinkRequest
{
    [Required]
    [StringLength(200)]
    public string ClientId { get; set; } = string.Empty;
}
