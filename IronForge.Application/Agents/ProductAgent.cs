using IronForge.Application.Agents.Approvals;
using IronForge.Application.Agents.Governance;
using IronForge.Application.Agents.Memory.Context;
using IronForge.Application.Agents.Memory.ConversationSession;
using IronForge.Application.Agents.Memory.Summarization;
using IronForge.Application.Agents.Memory.Tools;
using IronForge.Application.Agents.Models;
using IronForge.Application.Agents.Tools;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.Observablity.Logging;
using IronForge.Application.Observablity.Metrics;
using IronForge.Application.Observablity.Tracing;
using IronForge.Application.Products.Tools.Products;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace IronForge.Application.Agents;

public class ProductAgent : IProductAgent
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<ProductAgent> _logger;
    private readonly IGetProductsTool _getProductsTool;
    private readonly IGetProductTool _getProductTool;
    private readonly ICreateProductTool _createProductTool;
    private readonly IUpdateProductTool _updateProductTool;
    private readonly IDeleteProductTool _deleteProductTool;
    private readonly IAgentGovernanceService _agentGovernanceService;
    private readonly IAgentApprovalStore _approvalStore;
    private readonly IConversationService _conversationService;
    private readonly IExecutionContextAccessor _executionContext;
    private readonly IAgentConversationSessionAccessor _agentConversationSession;
    private readonly IConversationToolExecutionService _toolExecutionService;
    private readonly IConversationContextService _conversationContextService;
    private readonly IConversationSummaryCoordinator _summaryCoordinator;
    private readonly IApprovedToolExecutionService _approvedToolExecutionService;
    private readonly ITracingService _tracing;
    public ProductAgent(
        IChatClient chatClient,
        ILogger<ProductAgent> logger,
        IGetProductsTool getProductsTool,
        IGetProductTool getProductTool,
        ICreateProductTool createProductTool,
        IUpdateProductTool updateProductTool,
        IDeleteProductTool deleteProductTool,
        IAgentGovernanceService agentGovernanceService,
        IAgentApprovalStore approvalStore,
        IExecutionContextAccessor executionContext,
        IConversationService conversationService,
        IAgentConversationSessionAccessor agentConversationSession,
        IConversationToolExecutionService toolExecutionService,
        IConversationContextService conversationContextService,
        IConversationSummaryCoordinator summaryCoordinator,
        IApprovedToolExecutionService approvedToolExecutionService,
        ITracingService tracingService
        )
    {
        _chatClient = chatClient;
        _logger = logger;
        _getProductsTool = getProductsTool;
        _getProductTool = getProductTool;
        _createProductTool = createProductTool;
        _updateProductTool = updateProductTool;
        _deleteProductTool = deleteProductTool;
        _agentGovernanceService = agentGovernanceService;
        _approvalStore = approvalStore;
        _executionContext = executionContext;
        _conversationService = conversationService;
        _agentConversationSession = agentConversationSession;
        _toolExecutionService = toolExecutionService;
        _conversationContextService = conversationContextService;
        _summaryCoordinator = summaryCoordinator;
        _approvedToolExecutionService = approvedToolExecutionService;
        _tracing = tracingService;
    }

    public async Task<AgentRunResult> RunAsync(
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

            EstablishInternalAgentExecutionContext();

            var executionContext = _executionContext.Current 
                ?? throw new InvalidOperationException(
                     "Execution context could not be established.");

            var userId = executionContext.Actor.UserId;

            _logger.AgentExecutionStarted(sessionId, userId);

            var agent = executionContext?.Actor?.Agent;
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

            var agentMetricTag = IronForgeMetrics.AgentTag(agent.AgentId);
            IronForgeMetrics.AgentExecutions.Add(1, agentMetricTag);

            using var timer =
                MetricTimer.Start(
                    IronForgeMetrics.AgentDuration,
                    agentMetricTag);

            AgentSession session;

            if (sessionId.HasValue)
            {
                session = await _conversationService
                    .GetSessionAsync(
                        sessionId.Value,
                        cancellationToken)
                    ?? throw new InvalidOperationException(
                        "Conversation session was not found.");
            }
            else
            {
                session = await _conversationService
                    .CreateSessionAsync(
                        "product-agent",
                        cancellationToken: cancellationToken);
            }

            _agentConversationSession.Set(session);

            var messages =
                (await _conversationContextService.BuildContextAsync(
                    session.Id,
                    cancellationToken))
                .ToList();

            await _conversationService.AddMessageAsync(
              session.Id,
              ConversationMessageRole.User,
              userMessageText,
              cancellationToken: cancellationToken);

            messages.Add(
                   new ChatMessage(
                       ChatRole.User,
                       userMessageText));

            var tools = new[]
            {
            ProductAIFunctions.CreateGetProductsTool(
            _getProductsTool,
            agent,
            _agentGovernanceService,
            ProductAgentToolPolicies.GetProducts),

            ProductAIFunctions.CreateGetProductTool(
                _getProductTool,
                agent,
                _agentGovernanceService,
                ProductAgentToolPolicies.GetProduct),

            ProductAIFunctions.CreateCreateProductTool(
                _createProductTool,
                agent,
                _agentGovernanceService,
                ProductAgentToolPolicies.CreateProduct),

            ProductAIFunctions.CreateUpdateProductTool(
                _updateProductTool,
                agent,
                _agentGovernanceService,
                ProductAgentToolPolicies.UpdateProduct),

            ProductAIFunctions.CreateDeleteProductTool(
                _deleteProductTool,
                agent,
                _agentGovernanceService,
                ProductAgentToolPolicies.DeleteProduct)
        };

            var options = new ChatOptions
            {
                Tools = tools,
                AllowMultipleToolCalls = false
            };

            var agentClient = new ChatClientBuilder(_chatClient)
            .UseFunctionInvocation(
                configure: options =>
                {
                    options.MaximumIterationsPerRequest = 5;

                    options.FunctionInvoker =
                        (context, cancellationToken) =>
                            _toolExecutionService.InvokeAsync(
                                context,
                                cancellationToken);
                })
            .Build();

            var response =
          await agentClient.GetResponseAsync(
              messages,
              options,
              cancellationToken);

            var assistantText = response.Text;

            //Inspect approval requests
            var approvalRequest =
               response.Messages
                   .SelectMany(message => message.Contents)
                   .OfType<ToolApprovalRequestContent>()
                   .FirstOrDefault();

            if (approvalRequest is not null)
            {
                if (approvalRequest.ToolCall is not FunctionCallContent functionCall)
                {
                    throw new InvalidOperationException(
                        "The approval request does not contain a FunctionCallContent.");
                }

                var toolName = functionCall.Name;

                var policy =
                    ProductAgentToolPolicies.GetByToolName(
                        toolName);

                if (policy is null)
                {
                    throw new InvalidOperationException(
                        $"No governance policy exists for tool '{toolName}'.");
                }

                if (!policy.RequiresApproval)
                {
                    throw new InvalidOperationException(
                        $"Tool '{toolName}' generated an approval request " +
                        "but its governance policy does not require approval.");
                }

                if (agent is null)
                {
                    throw new InvalidOperationException(
                        "No agent identity is available.");
                }

                if (userId is null)
                {
                    throw new UnauthorizedAccessException(
                        "No authenticated user is available.");
                }

                /*
                 * Existing generic approval flow for tools that are not
                 * represented by a durable workflow.
                 */
                var approvalArgumentsJson =
                    JsonSerializer.Serialize(
                        functionCall.Arguments);

                var agentApproval = new AgentApproval
                {
                    ApprovalId =
                        Guid.NewGuid().ToString("N"),

                    RequestId =
                        approvalRequest.RequestId,

                    SessionId =
                        session.Id,

                    TenantId =
                        executionContext.TenantId
                        ?? throw new InvalidOperationException(
                            "A tenant is required for agent approval."),

                    UserId =
                        userId.Value,

                    AgentId =
                        agent.AgentId,

                    ToolName =
                        toolName,

                    ToolCallId =
                        functionCall.CallId,

                    ArgumentsJson =
                        approvalArgumentsJson,

                    RiskLevel =
                        policy.RiskLevel,

                    Status =
                        AgentApprovalStatus.Pending,

                    CreatedAtUtc =
                        DateTime.UtcNow,

                    ExpiresAtUtc =
                        DateTime.UtcNow.AddMinutes(10)
                };

                await _approvalStore.SaveAsync(
                    agentApproval,
                    cancellationToken);

                _logger.AgentApprovalCreated(
                         agentApproval.ApprovalId,
                         agentApproval.RequestId,
                         agentApproval.ToolName,
                         agentApproval.RiskLevel,
                         userId ?? 0, agent.AgentId);

                return new AgentRunResult
                {
                    SessionId = session.Id,
                    Status = AgentRunStatus.ApprovalRequired,
                    Message =
                        $"Approval is required to execute '{toolName}'.",
                    Approvals =
                    [
                        new AgentApprovalRequest
                    {
                        ApprovalId =
                            agentApproval.ApprovalId,

                        ToolName =
                            agentApproval.ToolName,

                        ArgumentsJson =
                            agentApproval.ArgumentsJson,

                        RiskLevel =
                            policy.RiskLevel.ToString(),

                        Description =
                            $"This operation requires human approval because " +
                            $"it is classified as {policy.RiskLevel} risk.",

                        CreatedAtUtc = agentApproval.CreatedAtUtc,

                        ExpiresAtUtc = agentApproval.ExpiresAtUtc
                    }
                    ]
                };
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
                Status =
                    AgentRunStatus.Completed,
                Message = assistantText
            };
        }
        catch
        {
            // We need the agent identity to tag the failure.
            var agentId =
                _executionContext.Current?
                    .Actor?
                    .Agent?
                    .AgentId;

            if (!string.IsNullOrWhiteSpace(agentId))
            {
                IronForgeMetrics.AgentFailures.Add(
                    1,
                    IronForgeMetrics.AgentTag(agentId));
            }

            throw;
        }
    }

    private void EstablishInternalAgentExecutionContext()
    {
        var currentContext = _executionContext.Current;
        if (currentContext == null)
        {
            throw new UnauthorizedAccessException(
                "A user execution context is required to run the internal Product Agent.");
        }

        var currentActor = currentContext.Actor;
        if (!currentActor.UserId.HasValue)
        {
            throw new UnauthorizedAccessException(
                "An authenticated user is required to run the internal Product Agent.");
        }

        _executionContext.SetAgent(
          new AgentIdentity
          {
              AgentId = "product-agent",
              Name = "IronForge Product Agent",
              Version = "1.0",
              Type = AgentType.Internal
          });
    }

    public async Task<AgentRunResult> RespondToApprovalAsync(
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

        EstablishInternalAgentExecutionContext();

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

        if (approval.Status != AgentApprovalStatus.Pending)
        {
            throw new InvalidOperationException(
                $"Approval '{approvalId}' has already been processed. " +
                $"Current status: {approval.Status}.");
        }

        var policy =
            ProductAgentToolPolicies.GetByToolName(
                approval.ToolName);

        if (policy is null)
        {
            throw new InvalidOperationException(
                $"No governance policy exists for tool '{approval.ToolName}'.");
        }

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

        _agentConversationSession.Set(session);

        if (approval.ExpiresAtUtc <= DateTime.UtcNow)
        {
            _logger.AgentApprovalExpired(
                approval.ApprovalId,
                approval.SessionId,
                approval.ToolName);

            approval.Status =
                AgentApprovalStatus.Expired;

            approval.DecisionReason =
                reason;

            approval.DecidedAtUtc =
                DateTime.UtcNow;

            await _approvalStore.UpdateAsync(
                approval,
                cancellationToken);

            return new AgentRunResult
            {
                SessionId = session.Id,
                Status = AgentRunStatus.Completed,
                Message =
                    "The requested operation has expired."
            };
        }

        if (!approved)
        {
            _logger.AgentApprovalRejected(
                approval.ApprovalId,
                approval.SessionId,
                approval.ToolName);

            approval.Status =
                AgentApprovalStatus.Rejected;

            approval.DecisionReason =
                reason;

            approval.DecidedAtUtc =
                DateTime.UtcNow;

            await _approvalStore.UpdateAsync(
                approval,
                cancellationToken);

            return new AgentRunResult
            {
                SessionId = session.Id,
                Status = AgentRunStatus.Completed,
                Message =
                    "The requested operation was rejected."
            };
        }

        /*
         * User approved the operation.
         */
        approval.Status =
            AgentApprovalStatus.Approved;

        approval.DecisionReason =
            reason;

        approval.DecidedAtUtc =
            DateTime.UtcNow;

        await _approvalStore.UpdateAsync(
            approval,
            cancellationToken);

        /*
         * Existing non-workflow approval path.
         */
        await _approvedToolExecutionService.ExecuteAsync(
            approval,
            cancellationToken);

        var messages =
            (await _conversationContextService.BuildContextAsync(
                session.Id,
                cancellationToken))
            .ToList();

        var tools = new[]
        {
        ProductAIFunctions.CreateGetProductsTool(
            _getProductsTool,
            agent,
            _agentGovernanceService,
            ProductAgentToolPolicies.GetProducts),

        ProductAIFunctions.CreateGetProductTool(
            _getProductTool,
            agent,
            _agentGovernanceService,
            ProductAgentToolPolicies.GetProduct),

        ProductAIFunctions.CreateCreateProductTool(
            _createProductTool,
            agent,
            _agentGovernanceService,
            ProductAgentToolPolicies.CreateProduct),

        ProductAIFunctions.CreateUpdateProductTool(
            _updateProductTool,
            agent,
            _agentGovernanceService,
            ProductAgentToolPolicies.UpdateProduct),

        ProductAIFunctions.CreateDeleteProductTool(
            _deleteProductTool,
            agent,
            _agentGovernanceService,
            ProductAgentToolPolicies.DeleteProduct)
    };

        var options = new ChatOptions
        {
            Tools = tools,
            AllowMultipleToolCalls = false
        };

        var agentClient =
            new ChatClientBuilder(_chatClient)
                .UseFunctionInvocation(
                    configure: options =>
                    {
                        options.MaximumIterationsPerRequest = 5;

                        options.FunctionInvoker =
                            (context, cancellationToken) =>
                                _toolExecutionService.InvokeAsync(
                                    context,
                                    cancellationToken);
                    })
                .Build();

        var response =
            await agentClient.GetResponseAsync(
                messages,
                options,
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
}