using System.ComponentModel.DataAnnotations;

namespace IronForge.Shared.Models.McpClients;

public sealed class CreateMcpClientRequest
{
    [Required]
    [StringLength(200)]
    public string ClientId { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [StringLength(100)]
    public string ClientType { get; set; } = "External";

    [Required]
    [StringLength(50)]
    public string Version { get; set; } = "1.0";

    public bool IsActive { get; set; } = true;
}
