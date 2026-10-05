using System.ComponentModel.DataAnnotations;

namespace IronForge.Shared.Models.McpClients;

public sealed class McpClientEditorModel
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

    public CreateMcpClientRequest ToCreateRequest() =>
        new()
        {
            ClientId = ClientId,
            Name = Name,
            Description = Description,
            ClientType = ClientType,
            Version = Version,
            IsActive = IsActive
        };

    public UpdateMcpClientRequest ToUpdateRequest() =>
        new()
        {
            Name = Name,
            Description = Description,
            ClientType = ClientType,
            Version = Version,
            IsActive = IsActive
        };

    public static McpClientEditorModel From(McpClientDto client) =>
        new()
        {
            ClientId = client.ClientId,
            Name = client.Name,
            Description = client.Description,
            ClientType = client.ClientType,
            Version = client.Version,
            IsActive = client.IsActive
        };
}
