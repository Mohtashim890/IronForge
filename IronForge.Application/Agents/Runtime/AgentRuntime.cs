using IronForge.Application.Agents.Approvals;
using IronForge.Application.Agents.Governance;
using IronForge.Application.Agents.Memory.Context;
using IronForge.Application.Agents.Memory.ConversationSession;
using IronForge.Application.Agents.Memory.Summarization;
using IronForge.Application.Agents.Memory.Tools;
using IronForge.Application.Agents.ModelProfiles;
using IronForge.Application.Agents.Models;
using IronForge.Application.Agents.Tools;
using IronForge.Application.Agents.Usage;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.Observablity.Logging;
using IronForge.Application.Observablity.Metrics;
using IronForge.Application.Observablity.Tracing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace IronForge.Application.Agents.Runtime;

/// <summary>
/// Generic execution engine for internal agents.
///
/// Everything agent-specific (identity, system prompt, tools, policies and
/// limits) comes from the <see cref="AgentDefinition"/>. The runtime owns the
/// flow: identity, session, context, model call with governed tools,
/// approvals, persistence, summarisation and telemetry.
/// </summary>
public sealed class AgentRuntime : IAgentRuntime
{
    private readonly IAgentDefinitionProvider _definitions;
    private readonly IAgentChatClientFactory _chatClients;
    private readonly IAgentQuotaService _quota;
    private readonly IAgentUsageRecorder _usage;
    private readonly ILogger<AgentRuntime> _logger;
    private readonly IAgentToolRegistry _toolRegistry;
    private readonly IAgentGovernanceService _governance;
    private readonly IAgentApprovalStore _approvalStore;
    private readonly IConversationService _conversationService;
    private readonly IExecutionContextAccessor _executionContext;
    private readonly IAgentConversationSessionAccessor _agentConversationSession;
    private readonly IConversationToolExecutionService _toolExecutionService;
    private readonly IConversationContextService _conversationContextService;
    private readonly IConversationSummaryCoordinator _summaryCoordinator;
    private readonly IApprovedToolExecutionService _approvedToolExecutionService;
    private readonly ITracingService _tracing;

    public AgentRuntime(
        IAgentDefinitionProvider definitions,
        IAgentChatClientFactory chatClients,
        IAgentQuotaService quota,
        IAgentUsageRecorder usage,
        ILogger<AgentRuntime> logger,
        IAgentToolRegistry toolRegistry,
        IAgentGovernanceService governance,
        IAgentApprovalStore approvalStore,
        IConversationService conversationService,
        IExecutionContextAccessor executionContext,
        IAgentConversationSessionAccessor agentConversationSession,
        IConversationToolExecutionService toolExecutionService,
        IConversationContextService conversationContextService,
        IConversationSummaryCoordinator summaryCoordinator,
        IApprovedToolExecutionService approvedToolExecutionService,
        ITracingService tracingService)
    {
        _definitions = definitions;
        _chatClients = chatClients;
        _quota = quota;
        _usage = usage;
        _logger = logger;
        _toolRegistry = toolRegistry;
        _governance = governance;
        _approvalStore = approvalStore;
        _conversationService = conversationService;
        _executionContext = executionContext;
        _agentConversationSession = agentConversationSession;
        _toolExecutionService = toolExecutionService;
        _conversationContextService = conversationContextService;
        _summaryCoordinator = summaryCoordinator;
        _approvedToolExecutionService = approvedToolExecutionService;
        _tracing = tracingService;
    }

    public async Task<AgentRunResult> RunAsync(
        string agentId,
        Guid? sessionId,
        string userMessageText,
        CancellationToken cancellationToken = default)
    {
        using var activity =
            _tracing.Start(
                "IronForge.Agent.Run");

        try
        {
            activity?.SetTag(
                "ironforge.agent.operation",
                "run");

            activity?.SetTag(
                "ironforge.session.id",
                sessionId);

            var definition =
                await _definitions.GetRequiredAsync(
                    agentId,
                    cancellationToken);

            EstablishInternalAgentExecutionContext(definition);

            var executionContext = _executionContext.Current
                ?? throw new InvalidOperationException(
                    "Execution context could not be established.");

            var userId = executionContext.Actor.UserId;

            _logger.AgentExecutionStarted(sessionId, userId);

            var agent = executionContext.Actor.Agent;
            if (agent is null || agent.AgentId is null)
            {
                throw new InvalidOperationException(
                    "No agent identity is available.");
            }

            if (userId is null)
            {
                throw new UnauthorizedAccessException(
                    "No authenticated user is available.");
            }

            // New work is refused once the tenant's monthly allowance is used.
            // Checked before anything is persisted or counted as an execution.
            await _quota.EnsureWithinQuotaAsync(cancellationToken);

            var agentMetricTag = IronForgeMetrics.AgentTag(agent.AgentId);
            IronForgeMetrics.AgentExecutions.Add(1, agentMetricTag);

            using var timer =
                MetricTimer.Start(
                    IronForgeMetrics.AgentDuration,
                    agentMetricTag);

            AgentSession session;

            if (sessionId.HasValue)
            {
                session = await GetSessionForAgentAsync(
                    definition,
                    sessionId.Value,
                    cancellationToken);
            }
            else
            {
                session = await _conversationService
                    .CreateSessionAsync(
                        definition.AgentId,
                        cancellationToken: cancellationToken);
            }

            _agentConversationSession.Set(session);

            var messages =
                await BuildModelMessagesAsync(
                    definition,
                    session.Id,
                    cancellationToken);

            await _conversationService.AddMessageAsync(
                session.Id,
                ConversationMessageRole.User,
                userMessageText,
                cancellationToken: cancellationToken);

            messages.Add(
                new ChatMessage(
                    ChatRole.User,
                    userMessageText));

            var response =
                await GetModelResponseAsync(
                    definition,
                    agent,
                    messages,
                    "run",
                    session.Id,
                    cancellationToken);

            var assistantText = response.Text;

            // Inspect approval requests.
            var approvalRequest =
                response.Messages
                    .SelectMany(message => message.Contents)
                    .OfType<ToolApprovalRequestContent>()
                    .FirstOrDefault();

            if (approvalRequest is not null)
            {
                return await CreateApprovalAsync(
                    definition,
                    agent,
                    executionContext,
                    userId.Value,
                    session,
                    approvalRequest,
                    cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(assistantText))
            {
                await _conversationService.AddMessageAsync(
                    session.Id,
                    ConversationMessageRole.Assistant,
                    assistantText,
                    cancellationToken: cancellationToken);
            }

            try
            {
                await _summaryCoordinator.EvaluateAndSummarizeAsync(
                    session.Id,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Conversation summarization failed for session {SessionId}",
                    session.Id);
            }

            _logger.AgentExecutionCompleted(
                session.Id,
                agent.AgentId);

            return new AgentRunResult
            {
                SessionId = session.Id,
                Status = AgentRunStatus.Completed,
                Message = assistantText
            };
        }
        catch (Exception ex) when (ex is not AgentQuotaExceededException)
        {
            // We need the agent identity to tag the failure.
            var failedAgentId =
                _executionContext.Current?
                    .Actor?
                    .Agent?
                    .AgentId;

            if (!string.IsNullOrWhiteSpace(failedAgentId))
            {
                IronForgeMetrics.AgentFailures.Add(
                    1,
                    IronForgeMetrics.AgentTag(failedAgentId));
            }

            throw;
        }
    }

    public async Task<AgentRunResult> RespondToApprovalAsync(
        string agentId,
        string approvalId,
        bool approved,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        _logger.AgentApprovalDecisionReceived(
            approvalId,
            approved);

        using var activity =
            _tracing.Start(
                "IronForge.Agent.Approval");

        activity?.SetTag(
            "ironforge.approval.id",
            approvalId);

        activity?.SetTag(
            "ironforge.approval.decision",
            approved ? "approved" : "rejected");

        var definition =
            await _definitions.GetRequiredAsync(
                agentId,
                cancellationToken);

        EstablishInternalAgentExecutionContext(definition);

        var executionContext =
            _executionContext.Current
            ?? throw new InvalidOperationException(
                "Execution context has not been established.");

        var agent =
            executionContext.Actor.Agent
            ?? throw new InvalidOperationException(
                "No agent identity is available.");

        var currentUserId =
            executionContext.Actor.UserId
            ?? throw new UnauthorizedAccessException(
                "Authenticated user is required.");

        var agentMetricTag = IronForgeMetrics.AgentTag(agent.AgentId);
        IronForgeMetrics.AgentExecutions.Add(1, agentMetricTag);

        using var timer =
            MetricTimer.Start(
                IronForgeMetrics.AgentDuration,
                agentMetricTag);

        var approval =
            await _approvalStore.GetAsync(
                approvalId,
                cancellationToken);

        if (approval is null)
        {
            throw new KeyNotFoundException(
                $"Approval '{approvalId}' was not found.");
        }

        if (approval.UserId != currentUserId)
        {
            throw new UnauthorizedAccessException(
                "This approval does not belong to the current user.");
        }

        if (approval.TenantId != executionContext.TenantId)
        {
            throw new UnauthorizedAccessException(
                "This approval does not belong to the current tenant.");
        }

        if (approval.AgentId != agent.AgentId)
        {
            throw new UnauthorizedAccessException(
                "This approval does not belong to the current agent.");
        }

        var needsSecondApprover =
            ApprovalRules.RequiresSecondApprover(approval.RiskLevel);

        // High risk: once a second approver has approved, the requester runs
        // (or cancels) it from here.
        var awaitingRun =
            needsSecondApprover &&
            ApprovalRules.HasSecondApproval(approval);

        if (approval.Status != AgentApprovalStatus.Pending && !awaitingRun)
        {
            throw new InvalidOperationException(
                $"Approval '{approvalId}' has already been processed. " +
                $"Current status: {approval.Status}.");
        }

        var policy =
            _toolRegistry.GetEffectivePolicy(
                definition,
                approval.ToolName);

        if (!policy.RequiresApproval)
        {
            throw new InvalidOperationException(
                $"Tool '{approval.ToolName}' does not require approval.");
        }

        var session =
            await _conversationService.GetSessionAsync(
                approval.SessionId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "The agent conversation associated with this approval no longer exists.");

        EnsureSessionBelongsToAgent(definition, session);

        _agentConversationSession.Set(session);

        if (approval.ExpiresAtUtc <= DateTime.UtcNow)
        {
            _logger.AgentApprovalExpired(
                approval.ApprovalId,
                approval.SessionId,
                approval.ToolName);

            approval.Status = AgentApprovalStatus.Expired;
            approval.DecisionReason = reason;
            approval.DecidedAtUtc = DateTime.UtcNow;

            await _approvalStore.UpdateAsync(
                approval,
                cancellationToken);

            return new AgentRunResult
            {
                SessionId = session.Id,
                Status = AgentRunStatus.Completed,
                Message = "The requested operation has expired."
            };
        }

        if (!approved)
        {
            _logger.AgentApprovalRejected(
                approval.ApprovalId,
                approval.SessionId,
                approval.ToolName);

            approval.Status = AgentApprovalStatus.Rejected;
            approval.DecisionReason = reason;
            approval.DecidedAtUtc = DateTime.UtcNow;
            approval.DecidedByUserId = currentUserId;

            await _approvalStore.UpdateAsync(
                approval,
                cancellationToken);

            return new AgentRunResult
            {
                SessionId = session.Id,
                Status = AgentRunStatus.Completed,
                Message = "The requested operation was rejected."
            };
        }

        if (needsSecondApprover)
        {
            // Never self-approved: only runs after another approver's decision.
            if (!awaitingRun)
            {
                throw new UnauthorizedAccessException(
                    $"'{approval.ToolName}' is a {approval.RiskLevel}-risk action and needs " +
                    "approval by another approver. It is waiting in Approvals.");
            }
        }
        else
        {
            // User approved the operation.
            approval.Status = AgentApprovalStatus.Approved;
            approval.DecisionReason = reason;
            approval.DecidedAtUtc = DateTime.UtcNow;
            approval.DecidedByUserId = currentUserId;

            await _approvalStore.UpdateAsync(
                approval,
                cancellationToken);
        }

        await _approvedToolExecutionService.ExecuteAsync(
            approval,
            cancellationToken);

        // Let the model report the outcome of the approved tool call.
        var messages =
            await BuildModelMessagesAsync(
                definition,
                session.Id,
                cancellationToken);

        // Not quota-checked: the human already approved this work, and the
        // follow-up only reports its outcome.
        var response =
            await GetModelResponseAsync(
                definition,
                agent,
                messages,
                "approval",
                session.Id,
                cancellationToken);

        var assistantText = response.Text;

        if (!string.IsNullOrWhiteSpace(assistantText))
        {
            await _conversationService.AddMessageAsync(
                session.Id,
                ConversationMessageRole.Assistant,
                assistantText,
                cancellationToken: cancellationToken);
        }

        return new AgentRunResult
        {
            SessionId = session.Id,
            Status = AgentRunStatus.Completed,
            Message = assistantText
        };
    }

    private void EstablishInternalAgentExecutionContext(
        AgentDefinition definition)
    {
        var currentContext = _executionContext.Current
            ?? throw new UnauthorizedAccessException(
                $"A user execution context is required to run '{definition.AgentId}'.");

        if (!currentContext.Actor.UserId.HasValue)
        {
            throw new UnauthorizedAccessException(
                $"An authenticated user is required to run '{definition.AgentId}'.");
        }

        // Both chat and approval responses come through here.
        if (!definition.AllowsRole(currentContext.Actor.Role))
        {
            throw new UnauthorizedAccessException(
                $"Your role cannot use agent '{definition.Name}'.");
        }

        _executionContext.SetAgent(
            definition.CreateIdentity());
    }

    /// <summary>
    /// Loads a session and verifies it belongs to this agent. A session that
    /// belongs to another agent is reported exactly like a missing session,
    /// so callers cannot use this to discover other agents' sessions.
    /// </summary>
    private async Task<AgentSession> GetSessionForAgentAsync(
        AgentDefinition definition,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var session =
            await _conversationService.GetSessionAsync(
                sessionId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Conversation session was not found.");

        EnsureSessionBelongsToAgent(definition, session);

        return session;
    }

    private void EnsureSessionBelongsToAgent(
        AgentDefinition definition,
        AgentSession session)
    {
        if (string.Equals(
                session.AgentId,
                definition.AgentId,
                StringComparison.Ordinal))
        {
            return;
        }

        _logger.LogWarning(
            "Session {SessionId} belongs to agent {SessionAgentId} " +
            "and cannot be used by agent {AgentId}.",
            session.Id,
            session.AgentId,
            definition.AgentId);

        throw new InvalidOperationException(
            "Conversation session was not found.");
    }

    /// <summary>
    /// System prompt first, then the persisted conversation context
    /// (summary and history). The system prompt is not persisted.
    /// </summary>
    private async Task<List<ChatMessage>> BuildModelMessagesAsync(
        AgentDefinition definition,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>();

        if (!string.IsNullOrWhiteSpace(definition.Instructions))
        {
            messages.Add(
                new ChatMessage(
                    ChatRole.System,
                    definition.Instructions));
        }

        messages.AddRange(
            await _conversationContextService.BuildContextAsync(
                sessionId,
                cancellationToken));

        return messages;
    }

    private async Task<ChatResponse> GetModelResponseAsync(
        AgentDefinition definition,
        AgentIdentity agent,
        IList<ChatMessage> messages,
        string operation,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var (chatClient, profile) =
            _chatClients.GetClient(definition.ModelProfile);

        var options = new ChatOptions
        {
            Tools = _toolRegistry.CreateModelTools(definition, agent),
            AllowMultipleToolCalls = definition.AllowMultipleToolCalls,
            MaxOutputTokens = profile.MaxOutputTokens
        };

        var agentClient =
            new ChatClientBuilder(chatClient)
                .UseFunctionInvocation(
                    configure: invocation =>
                    {
                        invocation.MaximumIterationsPerRequest =
                            definition.MaximumIterationsPerRequest;

                        invocation.FunctionInvoker =
                            (context, invocationCancellationToken) =>
                                _toolExecutionService.InvokeAsync(
                                    context,
                                    invocationCancellationToken);
                    })
                .Build();

        var response =
            await agentClient.GetResponseAsync(
                messages,
                options,
                cancellationToken);

        // Usage covers every function-calling round trip of this request.
        await _usage.RecordAsync(
            definition,
            profile,
            operation,
            sessionId,
            response.Usage,
            cancellationToken);

        return response;
    }

    private async Task<AgentRunResult> CreateApprovalAsync(
        AgentDefinition definition,
        AgentIdentity agent,
        Execution.ExecutionContext executionContext,
        int userId,
        AgentSession session,
        ToolApprovalRequestContent approvalRequest,
        CancellationToken cancellationToken)
    {
        if (approvalRequest.ToolCall is not FunctionCallContent functionCall)
        {
            throw new InvalidOperationException(
                "The approval request does not contain a FunctionCallContent.");
        }

        var toolName = functionCall.Name;

        var policy =
            _toolRegistry.GetEffectivePolicy(
                definition,
                toolName);

        if (!policy.RequiresApproval)
        {
            throw new InvalidOperationException(
                $"Tool '{toolName}' generated an approval request " +
                "but its governance policy does not require approval.");
        }

        // Evaluate governance with the real arguments before asking a human.
        // A call that would be denied at execution (missing agent permission,
        // amount over the limit) must not become an approval request.
        var preApprovalDecision =
            await _governance.EvaluateAsync(
                agent,
                policy,
                new AgentGovernanceContext
                {
                    ToolName = toolName,
                    Arguments =
                        new Dictionary<string, object?>(
                            functionCall.Arguments
                            ?? new Dictionary<string, object?>()),
                    ExecutionMode = AgentGovernanceExecutionMode.Normal
                },
                cancellationToken);

        if (preApprovalDecision.Decision == AgentGovernanceDecisionType.Deny)
        {
            _logger.LogWarning(
                "Approval for tool {ToolName} in session {SessionId} was not " +
                "requested because governance denied the call: {Reason}",
                toolName,
                session.Id,
                preApprovalDecision.Reason);

            var deniedMessage =
                $"The requested operation cannot be performed: {preApprovalDecision.Reason}";

            await _conversationService.AddMessageAsync(
                session.Id,
                ConversationMessageRole.Assistant,
                deniedMessage,
                cancellationToken: cancellationToken);

            return new AgentRunResult
            {
                SessionId = session.Id,
                Status = AgentRunStatus.Completed,
                Message = deniedMessage
            };
        }

        var createdAtUtc = DateTime.UtcNow;

        var agentApproval = new AgentApproval
        {
            ApprovalId = Guid.NewGuid().ToString("N"),
            RequestId = approvalRequest.RequestId,
            SessionId = session.Id,
            TenantId =
                executionContext.TenantId
                ?? throw new InvalidOperationException(
                    "A tenant is required for agent approval."),
            UserId = userId,
            AgentId = agent.AgentId,
            ToolName = toolName,
            ToolCallId = functionCall.CallId,
            ArgumentsJson = JsonSerializer.Serialize(functionCall.Arguments),
            RiskLevel = policy.RiskLevel,
            Status = AgentApprovalStatus.Pending,
            CreatedAtUtc = createdAtUtc,
            ExpiresAtUtc = createdAtUtc.Add(definition.ApprovalLifetime)
        };

        await _approvalStore.SaveAsync(
            agentApproval,
            cancellationToken);

        _logger.AgentApprovalCreated(
            agentApproval.ApprovalId,
            agentApproval.RequestId,
            agentApproval.ToolName,
            agentApproval.RiskLevel,
            userId,
            agent.AgentId);

        return new AgentRunResult
        {
            SessionId = session.Id,
            Status = AgentRunStatus.ApprovalRequired,
            Message = $"Approval is required to execute '{toolName}'.",
            Approvals =
            [
                new AgentApprovalRequest
                {
                    ApprovalId = agentApproval.ApprovalId,
                    ToolName = agentApproval.ToolName,
                    ArgumentsJson = agentApproval.ArgumentsJson,
                    RiskLevel = policy.RiskLevel.ToString(),
                    Description =
                        "This operation requires human approval because " +
                        $"it is classified as {policy.RiskLevel} risk.",
                    CreatedAtUtc = agentApproval.CreatedAtUtc,
                    ExpiresAtUtc = agentApproval.ExpiresAtUtc
                }
            ]
        };
    }
}
