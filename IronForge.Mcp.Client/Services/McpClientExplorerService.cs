using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Net.Http.Headers;

namespace IronForge.Mcp.Client.Services
{
    public sealed class McpClientExplorerService : IAsyncDisposable
    {
        private McpClient? _client;
        private HttpClient? _httpClient;

        public bool IsConnected =>
            _client is not null;

        public string? ServerName { get; private set; }

        public string? ServerVersion { get; private set; }

        public string? ProtocolVersion { get; private set; }

        public IReadOnlyList<McpClientTool> Tools { get; private set; }
            = [];

        public async Task ConnectAsync(
            string endpoint,
            string accessToken,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                throw new ArgumentException(
                    "MCP server endpoint is required.",
                    nameof(endpoint));
            }

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new ArgumentException(
                    "Access token is required.",
                    nameof(accessToken));
            }

            if (!Uri.TryCreate(
                    endpoint,
                    UriKind.Absolute,
                    out var endpointUri))
            {
                throw new ArgumentException(
                    "The MCP server endpoint is not a valid URI.",
                    nameof(endpoint));
            }

            await DisconnectAsync();

            var httpClient = new HttpClient();

            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    accessToken);

            try
            {
                var transport =
                    new HttpClientTransport(
                        new HttpClientTransportOptions
                        {
                            Endpoint = endpointUri,

                            TransportMode =
                                HttpTransportMode.StreamableHttp
                        },
                        httpClient);

                var client =
                    await McpClient.CreateAsync(
                        transport);

                var tools =
                    await client.ListToolsAsync();

                _httpClient = httpClient;
                _client = client;

                ServerName =
                    client.ServerInfo.Name;

                ServerVersion =
                    client.ServerInfo.Version;

                ProtocolVersion =
                    client.NegotiatedProtocolVersion;

                Tools =
                    tools.ToList();
            }
            catch
            {
                httpClient.Dispose();

                throw;
            }
        }

        public async Task DisconnectAsync()
        {
            var client = _client;
            var httpClient = _httpClient;

            _client = null;
            _httpClient = null;

            ServerName = null;
            ServerVersion = null;
            ProtocolVersion = null;
            Tools = [];

            if (client is not null)
            {
                await client.DisposeAsync();
            }

            httpClient?.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            await DisconnectAsync();
        }

        public async Task<CallToolResult> CallToolAsync(
            string toolName,
            IReadOnlyDictionary<string, object?> arguments,
            CancellationToken cancellationToken = default)
        {
            if (_client is null)
            {
                throw new InvalidOperationException(
                    "The MCP client is not connected.");
            }

            if (string.IsNullOrWhiteSpace(toolName))
            {
                throw new ArgumentException(
                    "Tool name is required.",
                    nameof(toolName));
            }

            return await _client.CallToolAsync(
                toolName,
                new Dictionary<string, object?>(
                    arguments,
                    StringComparer.Ordinal));
        }
    }
}
