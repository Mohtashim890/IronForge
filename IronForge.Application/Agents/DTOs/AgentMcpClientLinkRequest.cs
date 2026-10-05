using System.ComponentModel.DataAnnotations;

namespace IronForge.Application.Agents.DTOs;

public sealed class AgentMcpClientLinkRequest
{
    [Required]
    [StringLength(200)]
    public string ClientId { get; set; } = string.Empty;
}
