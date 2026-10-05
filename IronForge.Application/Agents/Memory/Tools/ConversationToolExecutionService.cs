using IronForge.Application.Agents.Memory.ConversationSession;
using IronForge.Application.Observablity.Metrics;
using IronForge.Application.Observablity.Tracing;
using Microsoft.Extensions.AI;
using System.Diagnostics;

namespace IronForge.Application.Agents.Memory.Tools
{
    public class ConversationToolExecutionService
    : IConversationToolExecutionService
    {
        private readonly IConversationService _conversationService;
        private readonly IAgentConversationSessionAccessor _sessionAccessor;
        private readonly IConversationToolContentMapper _toolContentMapper;
        private readonly ITracingService _tracing;

        public ConversationToolExecutionService(
            IConversationService conversationService,
            IAgentConversationSessionAccessor sessionAccessor,
            IConversationToolContentMapper contentMapper,
            ITracingService tracingService)
        {
            _conversationService = conversationService;
            _sessionAccessor = sessionAccessor;
            _toolContentMapper = contentMapper;
            _tracing = tracingService;
        }

        public async ValueTask<object?> InvokeAsync(
        FunctionInvocationContext context,
        CancellationToken cancellationToken = default)
        {
            var session = _sessionAccessor.Current
                ?? throw new InvalidOperationException(
                    "No agent conversation session is established.");

            var function = context.Function
                ?? throw new InvalidOperationException(
                    "Function invocation context does not contain a function.");

            using var activity =
                _tracing.Start(
                    IronForgeToolTracing.InvokeActivity);

            if (activity != null)
            {
                IronForgeToolTracing.SetToolIdentity(
                    activity,
                    function.Name,
                    context.CallContent?.CallId);

                activity.SetTag("ironforge.session.id", session.Id);
            }

            var toolName = function.Name;
            var toolNameTag = IronForgeMetrics.ToolTag(toolName);
            var pathTag = IronForgeMetrics.ToolPathTag("invoke");

            IronForgeMetrics.ToolExecutions.Add(
                1,
                toolNameTag,
                pathTag);

            using var timer =
                MetricTimer.Start(
                    IronForgeMetrics.ToolDuration,
                    toolNameTag,
                    pathTag);

            var callContent = context.CallContent;

            if (callContent != null)
            {
                var callJson =
                    _toolContentMapper.SerializeFunctionCall(
                        callContent);

                await _conversationService.AddMessageAsync(
                    session.Id,
                    ConversationMessageRole.Assistant,
                    callJson,
                    toolCallId: callContent.CallId,
                    toolName: function.Name,
                    metadataJson: """{"contentType":"functionCall"}""",
                    cancellationToken);
            }

            var resultModel = new ToolCallPersistenceModel();

            try
            {
                var result = await function.InvokeAsync(
                    context.Arguments,
                    cancellationToken);

                activity?.SetTag("ironforge.tool.execution_status", ToolExecutionStatuses.Completed);

                resultModel = new ToolCallPersistenceModel
                {
                    CallId = callContent?.CallId ?? "",
                    ToolName = function.Name,
                    Result = result is string text
                   ? text
                   : System.Text.Json.JsonSerializer.Serialize(result)
                };

                var resultJson =
                System.Text.Json.JsonSerializer.Serialize(
                    resultModel);

                await _conversationService.AddMessageAsync(
                    session.Id,
                    ConversationMessageRole.Tool,
                    resultJson,
                    toolCallId: callContent?.CallId,
                    toolName: function.Name,
                    metadataJson: """{"contentType":"functionResult"}""",
                    cancellationToken);

                return result;
            }
            catch (Exception)
            {
                activity?.SetTag("ironforge.tool.execution_status", ToolExecutionStatuses.Failed);
                activity?.SetStatus(
                    ActivityStatusCode.Error);
                IronForgeMetrics.ToolFailures.Add(
                   1,
                   toolNameTag,
                   pathTag);
            }

            return null;
        }
    }
}
