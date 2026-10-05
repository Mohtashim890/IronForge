using IronForge.Mcp.Client.Configurations;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using System.ClientModel;

namespace IronForge.Mcp.Client.Services;

public sealed class McpAgentService
{
    private readonly IConfiguration _configuration;
    private readonly AISettings _aiSettings;
    private readonly McpClientExplorerService _mcpClient;

    public McpAgentService(
        IConfiguration configuration,
        IOptions<AISettings> aiSettings,
        McpClientExplorerService mcpClient)
    {
        _configuration = configuration;
        _aiSettings = aiSettings.Value;
        _mcpClient = mcpClient;
    }

    public async Task<string> AskAsync(
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        if (!_mcpClient.IsConnected)
        {
            throw new InvalidOperationException(
                "The MCP client is not connected.");
        }

        if (string.IsNullOrWhiteSpace(userMessage))
        {
            throw new ArgumentException(
                "A message is required.",
                nameof(userMessage));
        }

#pragma warning disable OPENAI001

        var openAIClient =
            new OpenAIClient(
                new ApiKeyCredential(_aiSettings.ApiKey));

        var responsesClient =
            openAIClient.GetResponsesClient();

        var chatClient =
            responsesClient.AsIChatClient(_aiSettings.Model);

#pragma warning restore OPENAI001

        /*
         * McpClientTool inherits from AIFunction.
         *
         * Therefore the MCP tools discovered by
         * McpClientExplorerService can be supplied directly
         * to Microsoft.Extensions.AI.
         *
         * FunctionInvokingChatClient will:
         *
         *   1. Send the tool schemas to the LLM.
         *   2. Receive a function/tool call from the LLM.
         *   3. Invoke the corresponding McpClientTool.
         *   4. Send the MCP result back to the LLM.
         *   5. Continue until the LLM produces a final answer.
         */
        var agentClient =
            new ChatClientBuilder(chatClient)
                .UseFunctionInvocation(
                    configure: options =>
                    {
                        options.MaximumIterationsPerRequest = 5;

                        /*
                         * MCP operations are executed against a remote
                         * server. Keep invocation deterministic for this
                         * client rather than allowing concurrent tool
                         * execution.
                         */
                        options.AllowConcurrentInvocation = false;

                        /*
                         * Fail the function-calling loop if the model
                         * requests a function that is not one of the
                         * supplied MCP tools.
                         */
                        options.TerminateOnUnknownCalls = true;
                    })
                .Build();

        var messages =
            new List<ChatMessage>
            {
                new(
                    ChatRole.System,
                    """
                    You are an external IronForge assistant.

                    You have access to the tools exposed by the
                    connected IronForge MCP server.

                    Use an MCP tool whenever the user's request
                    requires information or an action that a
                    connected tool can perform.

                    The MCP tool definitions supplied to you contain
                    the authoritative names, descriptions, and input
                    schemas for the available tools.

                    When calling an MCP tool:

                    - Use the exact tool name supplied by the MCP server.
                    - Construct arguments according to the tool's
                      input schema.
                    - Do not invent argument names.
                    - Do not omit required arguments.
                    - Do not invent product identifiers or product data.
                    - For create or update operations, use the object
                      structure defined by the tool schema.
                    - For destructive operations such as deletion,
                      only invoke the tool when the user's request
                      clearly asks for that action.

                    If an IronForge MCP tool reports an authorization,
                    validation, not-found, or execution failure,
                    explain the failure to the user.

                    Do not claim that an operation succeeded unless
                    the MCP tool returned a successful result.
                    """),

                new(
                    ChatRole.User,
                    userMessage)
            };

        var tools =
            _mcpClient.Tools;

        if (tools.Count == 0)
        {
            throw new InvalidOperationException(
                "The connected MCP server has not exposed any tools.");
        }

        var response =
            await agentClient.GetResponseAsync(
                messages,
                new ChatOptions
                {
                    Tools =
                        [.. tools]
                },
                cancellationToken);

        return string.IsNullOrWhiteSpace(response.Text)
            ? "The assistant did not return a response."
            : response.Text;
    }
}