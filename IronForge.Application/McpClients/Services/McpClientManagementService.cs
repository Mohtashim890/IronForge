using IronForge.Application.Auth.Services;
using IronForge.Application.Commons;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.McpClients.DTOs;
using IronForge.Application.Persistence;

namespace IronForge.Application.McpClients.Services;

public sealed class McpClientManagementService
    : IMcpClientManagementService
{
    private readonly IRepository<McpClient> _clients;
    private readonly IUserAuthorizationService _authorization;
    private readonly IExecutionContextAccessor _executionContextAccessor;

    public McpClientManagementService(
        IRepository<McpClient> clients,
        IUserAuthorizationService authorization,
        IExecutionContextAccessor executionContextAccessor)
    {
        _clients = clients;
        _authorization = authorization;
        _executionContextAccessor = executionContextAccessor;
    }

    public async Task<ServiceResult<List<McpClientDto>>> GetClientsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_authorization.HasPermission(Permissions.McpClientsRead))
        {
            return ServiceResult<List<McpClientDto>>.Fail(
                "You are not authorized to view MCP clients.",
                403);
        }

        var clients = await _clients.ListAsync(cancellationToken);

        return ServiceResult<List<McpClientDto>>.Ok(
            clients
                .OrderBy(x => x.Name)
                .Select(Map)
                .ToList());
    }

    public async Task<ServiceResult<McpClientDto>> GetClientAsync(
        string clientId,
        CancellationToken cancellationToken = default)
    {
        if (!_authorization.HasPermission(Permissions.McpClientsRead))
        {
            return ServiceResult<McpClientDto>.Fail(
                "You are not authorized to view MCP clients.",
                403);
        }

        if (string.IsNullOrWhiteSpace(clientId))
        {
            return ServiceResult<McpClientDto>.Fail(
                "Client ID is required.",
                400);
        }

        var client = await _clients.FirstOrDefaultAsync(
            x => x.ClientId == clientId,
            cancellationToken);

        if (client is null)
        {
            return ServiceResult<McpClientDto>.Fail(
                "MCP client not found.",
                404);
        }

        return ServiceResult<McpClientDto>.Ok(Map(client));
    }

    public async Task<ServiceResult<McpClientDto>> CreateClientAsync(
        CreateMcpClientRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_authorization.HasPermission(Permissions.McpClientsCreate))
        {
            return ServiceResult<McpClientDto>.Fail(
                "You are not authorized to create MCP clients.",
                403);
        }

        var normalizedClientId = request.ClientId.Trim();

        if (string.IsNullOrWhiteSpace(normalizedClientId))
        {
            return ServiceResult<McpClientDto>.Fail(
                "Client ID is required.",
                400);
        }

        var exists = await _clients.AnyAsync(
            x => x.ClientId == normalizedClientId,
            cancellationToken);

        if (exists)
        {
            return ServiceResult<McpClientDto>.Fail(
                "An MCP client with this ID already exists.",
                409);
        }

        var client = new McpClient
        {
            TenantId = ResolveTenantId(),
            ClientId = normalizedClientId,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            ClientType = NormalizeClientType(request.ClientType),
            Version = request.Version.Trim(),
            IsActive = request.IsActive,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _clients.Add(client);

        await _clients.SaveChangesAsync(cancellationToken);

        return ServiceResult<McpClientDto>.Ok(Map(client));
    }

    public async Task<ServiceResult<McpClientDto>> UpdateClientAsync(
        string clientId,
        UpdateMcpClientRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_authorization.HasPermission(Permissions.McpClientsUpdate))
        {
            return ServiceResult<McpClientDto>.Fail(
                "You are not authorized to update MCP clients.",
                403);
        }

        if (string.IsNullOrWhiteSpace(clientId))
        {
            return ServiceResult<McpClientDto>.Fail(
                "Client ID is required.",
                400);
        }

        var client = await _clients.FirstOrDefaultTrackedAsync(
            x => x.ClientId == clientId,
            cancellationToken);

        if (client is null)
        {
            return ServiceResult<McpClientDto>.Fail(
                "MCP client not found.",
                404);
        }

        client.Name = request.Name.Trim();
        client.Description = request.Description?.Trim();
        client.ClientType = NormalizeClientType(request.ClientType);
        client.Version = request.Version.Trim();
        client.IsActive = request.IsActive;
        client.UpdatedAtUtc = DateTime.UtcNow;

        //_clients.Update(client);

        await _clients.SaveChangesAsync(cancellationToken);

        return ServiceResult<McpClientDto>.Ok(Map(client));
    }

    public async Task<ServiceResult<bool>> DeleteClientAsync(
        string clientId,
        CancellationToken cancellationToken = default)
    {
        if (!_authorization.HasPermission(Permissions.McpClientsDelete))
        {
            return ServiceResult<bool>.Fail(
                "You are not authorized to delete MCP clients.",
                403);
        }

        if (string.IsNullOrWhiteSpace(clientId))
        {
            return ServiceResult<bool>.Fail(
                "Client ID is required.",
                400);
        }

        var client = await _clients.FirstOrDefaultAsync(
            x => x.ClientId == clientId,
            cancellationToken);

        if (client is null)
        {
            return ServiceResult<bool>.Fail(
                "MCP client not found.",
                404);
        }

        _clients.Remove(client);

        await _clients.SaveChangesAsync(cancellationToken);

        return ServiceResult<bool>.Ok(true);
    }

    private int ResolveTenantId()
    {
        return _executionContextAccessor.Current?.TenantId ??
        throw new InvalidOperationException(
            "MCP client creation requires an established tenant execution context.");
    }

    private static string NormalizeClientType(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "External"
            : value.Trim();
    }

    private static McpClientDto Map(McpClient client)
    {
        return new McpClientDto
        {
            Id = client.Id,
            ClientId = client.ClientId,
            Name = client.Name,
            Description = client.Description,
            ClientType = client.ClientType,
            Version = client.Version,
            IsActive = client.IsActive,
            CreatedAtUtc = client.CreatedAtUtc,
            UpdatedAtUtc = client.UpdatedAtUtc
        };
    }
}
