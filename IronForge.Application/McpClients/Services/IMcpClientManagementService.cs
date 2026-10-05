using IronForge.Application.Commons;
using IronForge.Application.McpClients.DTOs;

namespace IronForge.Application.McpClients.Services;

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
