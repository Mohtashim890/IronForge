namespace IronForge.Shared.Models.McpClients;

public sealed class McpClientDto
{
    public int Id { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ClientType { get; set; } = "External";
    public string Version { get; set; } = "1.0";
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
