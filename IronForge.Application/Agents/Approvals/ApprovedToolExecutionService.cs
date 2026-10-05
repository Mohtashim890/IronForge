using IronForge.Application.Agents.Memory.ConversationSession;
using IronForge.Application.Agents.Memory.Tools;
using IronForge.Application.Agents.Tools;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.Observablity.Metrics;
using IronForge.Application.Observablity.Tracing;
using Microsoft.Extensions.AI;
using System.Diagnostics;
using System.Text.Json;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace IronForge.Application.Agents.Approvals;

public class ApprovedToolExecutionService
    : IApprovedToolExecutionService
{
    private readonly IAgentToolResolver _toolResolver;
    private readonly IExecutionContextAccessor _executionContext;
    private readonly IConversationService _conversationService;
    private readonly IAgentConversationSessionAccessor _sessionAccessor;
    private readonly IApprovalExecutionAuthorizationService _authorizationService;
    private readonly IConversationToolContentMapper _toolContentMapper;
    private readonly IAgentApprovalStore _approvalStore;
    private readonly ITracingService _tracing;

    public ApprovedToolExecutionService(
        IAgentToolResolver toolResolver,
        IExecutionContextAccessor executionContext,
        IConversationService conversationService,
        IAgentConversationSessionAccessor sessionAccessor,
        IApprovalExecutionAuthorizationService authorizationService,
        IConversationToolContentMapper toolContentMapper,
        IAgentApprovalStore approvalStore,
        ITracingService tracingService)
    {
        _toolResolver = toolResolver;
        _executionContext = executionContext;
        _conversationService = conversationService;
        _sessionAccessor = sessionAccessor;
        _authorizationService = authorizationService;
        _toolContentMapper = toolContentMapper;
        _approvalStore = approvalStore;
        _tracing = tracingService;
    }

    public async Task<object?> ExecuteAsync(
        AgentApproval approval,
        CancellationToken cancellationToken = default)
    {
        using var activity = _tracing.Start(
         IronForgeToolTracing.ExecuteActivity);


        if (approval is null)
        {
            throw new ArgumentNullException(nameof(approval));
        }

        if (activity != null)
        {
            IronForgeToolTracing.SetToolIdentity(
                activity,
                approval.ToolName,
                approval.ToolCallId);

            activity.SetTag(
                "ironforge.approval.id",
                approval.ApprovalId);

            activity.SetTag(
                "ironforge.session.id",
                approval.SessionId);
        }

        var authorization =
            await _authorizationService.AuthorizeAsync(
                approval,
                cancellationToken);

        if (!authorization.IsAllowed)
        {
            throw new UnauthorizedAccessException(
                authorization.Reason);
        }

        var toolName = approval.ToolName;
        var toolNameTag = IronForgeMetrics.ToolTag(toolName);
        var pathTag = IronForgeMetrics.ToolPathTag("execute");

        IronForgeMetrics.ToolExecutions.Add(
            1,
            toolNameTag,
            pathTag);

        using var timer =
            MetricTimer.Start(
                IronForgeMetrics.ToolDuration,
                toolNameTag,
                pathTag);

        try
        {
            approval.Status = AgentApprovalStatus.Executing;
            await _approvalStore.UpdateAsync(
                approval,
                cancellationToken);

            var session =
                _sessionAccessor.Current
                ?? throw new InvalidOperationException(
                    "No agent conversation session is established.");

            var executionContext =
                _executionContext.Current
                ?? throw new InvalidOperationException(
                    "Execution context has not been established.");

            var agent =
                executionContext.Actor.Agent
                ?? throw new InvalidOperationException(
                    "No agent identity is available.");

            if (session.Id != approval.SessionId)
            {
                throw new InvalidOperationException(
                    "The current agent conversation does not match the approval session.");
            }

            if (string.IsNullOrWhiteSpace(approval.ToolName))
            {
                throw new InvalidOperationException(
                    "Approval does not contain a tool name.");
            }

            if (string.IsNullOrWhiteSpace(approval.ToolCallId))
            {
                throw new InvalidOperationException(
                    "Approval does not contain a tool call ID.");
            }

            if (string.IsNullOrWhiteSpace(approval.ArgumentsJson))
            {
                throw new InvalidOperationException(
                    "Approval does not contain tool arguments.");
            }

            var argumentDictionary = JsonSerializer.Deserialize<Dictionary<string, object?>>(
            approval.ArgumentsJson);

            if (argumentDictionary is null)
            {
                throw new InvalidOperationException(
                    "Approved tool arguments could not be reconstructed.");
            }

            await _conversationService.AddMessageAsync(
                session.Id,
                ConversationMessageRole.Assistant,
                _toolContentMapper.SerializeFunctionCall(approval.ToolName, approval.ToolCallId, argumentDictionary),
                toolCallId: approval.ToolCallId,
                toolName: approval.ToolName,
                metadataJson: """{"contentType":"functionCall"}""",
                cancellationToken);

            var tool =
              _toolResolver.Resolve(
                  approval.ToolName,
                  agent);

            var arguments = new AIFunctionArguments(
                argumentDictionary);

            var result = await tool.InvokeAsync(
                arguments,
                cancellationToken);

            activity?.SetTag(
                "ironforge.tool.execution_status",
                ToolExecutionStatuses.Completed);

            var resultModel = new ToolCallPersistenceModel
            {
                CallId = approval.ToolCallId,
                ToolName = approval.ToolName,
                Result = result is string text
            ? text
            : JsonSerializer.Serialize(result)
            };

            var resultJson = JsonSerializer.Serialize(resultModel);
            await _conversationService.AddMessageAsync(
                session.Id,
                ConversationMessageRole.Tool,
                resultJson,
                toolCallId: approval.ToolCallId,
                toolName: approval.ToolName,
                metadataJson: """{"contentType":"functionResult"}""",
                cancellationToken);

            approval.Status = AgentApprovalStatus.Executed;
            approval.ExecutedAtUtc = DateTime.UtcNow;
            await _approvalStore.UpdateAsync(
                approval,
                cancellationToken);

            return result;
        }
        catch
        {
            activity?.SetStatus(
               ActivityStatusCode.Error);

            activity?.SetTag(
                "ironforge.tool.execution_status",
                ToolExecutionStatuses.Failed);

            approval.Status = AgentApprovalStatus.ExecutionFailed;
            approval.ExecutedAtUtc = DateTime.UtcNow;
            await _approvalStore.UpdateAsync(
                approval,
                cancellationToken);

            IronForgeMetrics.ToolFailures.Add(
                  1,
                  toolNameTag,
                  pathTag);

            throw;
        }
    }
}