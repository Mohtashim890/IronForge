using IronForge.Shared.Models;
using IronForge.Shared.Models.McpClients;

namespace IronForge.Shared.Services;

public interface IMcpClientManagementService
{
    Task<ServiceResult<List<McpClientDto>>> GetClientsAsync(
        CancellationToken cancellationToken = default);

    Task<ServiceResult<McpClientDto>> GetClientAsync(
        string clientId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<McpClientDto>> CreateClientAsync(
        CreateMcpClientRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<McpClientDto>> UpdateClientAsync(
        string clientId,
        UpdateMcpClientRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<bool>> DeleteClientAsync(
        string clientId,
        CancellationToken cancellationToken = default);
}
