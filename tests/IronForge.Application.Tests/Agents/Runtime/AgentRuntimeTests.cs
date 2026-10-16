using IronForge.Application.Agents.Approvals;
using IronForge.Application.Agents.Governance;
using IronForge.Application.Agents.Memory.Context;
using IronForge.Application.Agents.Memory.ConversationSession;
using IronForge.Application.Agents.Memory.Summarization;
using IronForge.Application.Agents.Memory.Tools;
using IronForge.Application.Agents.ModelProfiles;
using IronForge.Application.Agents.Models;
using IronForge.Application.Agents.Runtime;
using IronForge.Application.Agents.Tools;
using IronForge.Application.Agents.Usage;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.Observablity.Tracing;
using IronForge.Application.Tests.Agents.Delegations;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace IronForge.Application.Tests.Agents.Runtime;

public class AgentRuntimeTests
{
    private readonly Mock<IChatClient> _chatClient = new();
    private readonly Mock<IAgentChatClientFactory> _chatClients = new();
    private readonly Mock<IAgentQuotaService> _quota = new();
    private readonly Mock<IAgentUsageRecorder> _usage = new();
    private readonly Mock<IAgentToolRegistry> _toolRegistry = new();
    private readonly Mock<IAgentGovernanceService> _governance = new();
    private readonly Mock<IAgentApprovalStore> _approvalStore = new();
    private readonly Mock<IConversationService> _conversationService = new();
    private readonly Mock<IAgentConversationSessionAccessor> _sessionAccessor = new();
    private readonly Mock<IConversationToolExecutionService> _toolExecution = new();
    private readonly Mock<IConversationContextService> _contextService = new();
    private readonly Mock<IConversationSummaryCoordinator> _summaryCoordinator = new();
    private readonly Mock<IApprovedToolExecutionService> _approvedExecution = new();
    private readonly Mock<ITracingService> _tracing = new();
    // Not readonly: a test may replace it with a context for another role.
    private ExecutionContextAccessor _executionContext = new();

    private IEnumerable<ChatMessage>? _sentMessages;
    private ChatOptions? _sentOptions;

    public AgentRuntimeTests()
    {
        _executionContext.Set(
            ApplicationTestContext.AuthenticatedUser());

        _chatClients
            .Setup(x => x.GetClient(It.IsAny<string?>()))
            .Returns((string? profile) =>
                (_chatClient.Object,
                 new AgentModelProfile(profile ?? "standard", "test-model", 4000, null)));

        _toolRegistry
            .Setup(x => x.CreateModelTools(It.IsAny<AgentDefinition>(), It.IsAny<AgentIdentity>()))
            .Returns((AgentDefinition definition, AgentIdentity _) =>
                definition.Tools
                    .Select(tool => AIFunctionFactory.Create(() => "ok", tool.ToolName))
                    .ToList<AITool>());

        _toolRegistry
            .Setup(x => x.GetEffectivePolicy(It.IsAny<AgentDefinition>(), It.IsAny<string>()))
            .Returns((AgentDefinition _, string toolName) =>
                new AgentToolPolicy { ToolName = toolName, RequiresApproval = true });

        _contextService
            .Setup(x => x.BuildContextAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChatMessage>
            {
                new(ChatRole.User, "earlier question"),
                new(ChatRole.Assistant, "earlier answer")
            });

        _chatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback((IEnumerable<ChatMessage> messages, ChatOptions? options, CancellationToken _) =>
            {
                _sentMessages = messages.ToList();
                _sentOptions = options;
            })
            .ReturnsAsync(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, "Here are your products."))
            {
                Usage = new UsageDetails
                {
                    InputTokenCount = 120,
                    OutputTokenCount = 30,
                    TotalTokenCount = 150
                }
            });
    }

    private AgentRuntime CreateRuntime(IAgentDefinitionProvider? definitions = null)
    {
        return new AgentRuntime(
            definitions ?? new CodeAgentDefinitionProvider(),
            _chatClients.Object,
            _quota.Object,
            _usage.Object,
            NullLogger<AgentRuntime>.Instance,
            _toolRegistry.Object,
            _governance.Object,
            _approvalStore.Object,
            _conversationService.Object,
            _executionContext,
            _sessionAccessor.Object,
            _toolExecution.Object,
            _contextService.Object,
            _summaryCoordinator.Object,
            _approvedExecution.Object,
            _tracing.Object);
    }

    private static AgentSession Session(string agentId)
    {
        return new AgentSession
        {
            Id = Guid.NewGuid(),
            TenantId = ApplicationTestContext.DefaultTenantId,
            UserId = ApplicationTestContext.DefaultUserId,
            AgentId = agentId
        };
    }

    [Fact]
    public async Task ProductAgentDefinition_KeepsExistingBehaviourSettings()
    {
        var definition = await new CodeAgentDefinitionProvider()
            .GetRequiredAsync(AgentIds.ProductAgent);

        Assert.Equal(5, definition.MaximumIterationsPerRequest);
        Assert.False(definition.AllowMultipleToolCalls);
        Assert.Equal(TimeSpan.FromMinutes(10), definition.ApprovalLifetime);
        Assert.Equal(
            ["get_products", "get_product", "create_product", "update_product", "delete_product"],
            definition.Tools.Select(tool => tool.ToolName));
        Assert.False(string.IsNullOrWhiteSpace(definition.Instructions));
    }

    [Fact]
    public async Task DefinitionProvider_UnknownAgent_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new CodeAgentDefinitionProvider().GetRequiredAsync("unknown-agent"));
    }

    [Fact]
    public async Task RunAsync_NewSession_SendsSystemPromptFirstAndDefinitionTools()
    {
        var session = Session(AgentIds.ProductAgent);

        _conversationService
            .Setup(x => x.CreateSessionAsync(AgentIds.ProductAgent, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var result = await CreateRuntime().RunAsync(
            AgentIds.ProductAgent,
            sessionId: null,
            "list my products");

        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.Equal(session.Id, result.SessionId);
        Assert.Equal("Here are your products.", result.Message);

        // System prompt first, then persisted context, then the new message.
        var messages = Assert.IsAssignableFrom<IList<ChatMessage>>(_sentMessages);
        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Equal(AgentDefinitions.ProductAgent.Instructions, messages[0].Text);
        Assert.Equal("earlier question", messages[1].Text);
        Assert.Equal("list my products", messages[^1].Text);

        // The model is offered exactly the definition's tools.
        Assert.NotNull(_sentOptions?.Tools);
        Assert.Equal(
            AgentDefinitions.ProductAgent.Tools.Select(tool => tool.ToolName),
            _sentOptions!.Tools!.Select(tool => tool.Name));

        // The agent identity comes from the definition.
        var agent = _executionContext.Current!.Actor.Agent!;
        Assert.Equal(AgentIds.ProductAgent, agent.AgentId);
        Assert.Equal(AgentType.Internal, agent.Type);

        // The system prompt is never persisted; user and assistant text are.
        _conversationService.Verify(x => x.AddMessageAsync(
            session.Id, ConversationMessageRole.User, "list my products",
            null, null, null, It.IsAny<CancellationToken>()), Times.Once);
        _conversationService.Verify(x => x.AddMessageAsync(
            session.Id, ConversationMessageRole.Assistant, "Here are your products.",
            null, null, null, It.IsAny<CancellationToken>()), Times.Once);
        _conversationService.Verify(x => x.AddMessageAsync(
            It.IsAny<Guid>(), It.IsAny<ConversationMessageRole>(),
            AgentDefinitions.ProductAgent.Instructions,
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_SessionOfAnotherAgent_IsRejectedLikeAMissingSession()
    {
        var foreignSession = Session("another-agent");

        _conversationService
            .Setup(x => x.GetSessionAsync(foreignSession.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(foreignSession);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateRuntime().RunAsync(
                AgentIds.ProductAgent,
                foreignSession.Id,
                "list my products"));

        Assert.Equal("Conversation session was not found.", exception.Message);

        // Nothing is persisted and the model is never called.
        _conversationService.Verify(x => x.AddMessageAsync(
            It.IsAny<Guid>(), It.IsAny<ConversationMessageRole>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _chatClient.Verify(x => x.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RespondToApprovalAsync_SessionOfAnotherAgent_IsRejectedBeforeExecution()
    {
        var foreignSession = Session("another-agent");

        var approval = new AgentApproval
        {
            ApprovalId = "approval-1",
            SessionId = foreignSession.Id,
            TenantId = ApplicationTestContext.DefaultTenantId,
            UserId = ApplicationTestContext.DefaultUserId,
            AgentId = AgentIds.ProductAgent,
            ToolName = "delete_product",
            Status = AgentApprovalStatus.Pending,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5)
        };

        _approvalStore
            .Setup(x => x.GetAsync("approval-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(approval);

        _conversationService
            .Setup(x => x.GetSessionAsync(foreignSession.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(foreignSession);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateRuntime().RespondToApprovalAsync(
                AgentIds.ProductAgent,
                "approval-1",
                approved: true));

        Assert.Equal(AgentApprovalStatus.Pending, approval.Status);
        _approvedExecution.Verify(x => x.ExecuteAsync(
            It.IsAny<AgentApproval>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_UsesTheAgentsModelProfileAndRecordsUsage()
    {
        var session = Session(AgentIds.InsightsAgent);

        _conversationService
            .Setup(x => x.CreateSessionAsync(AgentIds.InsightsAgent, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        await CreateRuntime().RunAsync(
            AgentIds.InsightsAgent,
            sessionId: null,
            "what changed this week?");

        // The definition's profile selects the client and its output cap.
        _chatClients.Verify(x => x.GetClient("economy"), Times.Once);
        Assert.Equal(4000, _sentOptions?.MaxOutputTokens);

        _usage.Verify(x => x.RecordAsync(
            It.Is<AgentDefinition>(d => d.AgentId == AgentIds.InsightsAgent),
            It.Is<AgentModelProfile>(p => p.Name == "economy"),
            "run",
            session.Id,
            It.Is<UsageDetails?>(u => u != null && u.TotalTokenCount == 150),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_QuotaExceeded_RefusesBeforePersistingOrCallingTheModel()
    {
        _quota
            .Setup(x => x.EnsureWithinQuotaAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AgentQuotaExceededException(1000, 1000, DateTime.UtcNow.AddDays(3)));

        await Assert.ThrowsAsync<AgentQuotaExceededException>(
            () => CreateRuntime().RunAsync(
                AgentIds.ProductAgent,
                sessionId: null,
                "list my products"));

        _conversationService.Verify(x => x.CreateSessionAsync(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _chatClient.Verify(x => x.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _usage.Verify(x => x.RecordAsync(
            It.IsAny<AgentDefinition>(), It.IsAny<AgentModelProfile>(), It.IsAny<string>(),
            It.IsAny<Guid?>(), It.IsAny<UsageDetails?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- Four-eyes approvals ----------------------------------------------

    private AgentApproval PendingApproval(AgentRiskLevel risk, string toolName)
    {
        var session = Session(AgentIds.ProductAgent);

        _conversationService
            .Setup(x => x.GetSessionAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var approval = new AgentApproval
        {
            ApprovalId = Guid.NewGuid().ToString("N"),
            SessionId = session.Id,
            TenantId = ApplicationTestContext.DefaultTenantId,
            UserId = ApplicationTestContext.DefaultUserId,
            AgentId = AgentIds.ProductAgent,
            ToolName = toolName,
            ArgumentsJson = "{}",
            RiskLevel = risk,
            Status = AgentApprovalStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10)
        };

        _approvalStore
            .Setup(x => x.GetAsync(approval.ApprovalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(approval);

        return approval;
    }

    [Theory]
    [InlineData(AgentRiskLevel.High)]
    [InlineData(AgentRiskLevel.Critical)]
    public async Task HighRiskAction_CannotBeApprovedByTheRequester(AgentRiskLevel risk)
    {
        var approval = PendingApproval(risk, "delete_product");

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CreateRuntime().RespondToApprovalAsync(
                AgentIds.ProductAgent,
                approval.ApprovalId,
                approved: true));

        Assert.Contains("needs approval by another approver", exception.Message);
        Assert.Equal(AgentApprovalStatus.Pending, approval.Status);

        _approvedExecution.Verify(
            x => x.ExecuteAsync(It.IsAny<AgentApproval>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HighRiskAction_ApprovedByAnotherApprover_IsRunByTheRequester()
    {
        var approval = PendingApproval(AgentRiskLevel.High, "delete_product");

        approval.Status = AgentApprovalStatus.Approved;
        approval.DecidedByUserId = ApplicationTestContext.DefaultUserId + 1;

        var result = await CreateRuntime().RespondToApprovalAsync(
            AgentIds.ProductAgent,
            approval.ApprovalId,
            approved: true);

        Assert.Equal(AgentRunStatus.Completed, result.Status);

        // The second approver's decision is kept, not overwritten by the requester.
        Assert.Equal(ApplicationTestContext.DefaultUserId + 1, approval.DecidedByUserId);

        _approvedExecution.Verify(
            x => x.ExecuteAsync(approval, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HighRiskAction_CanBeCancelledByTheRequester()
    {
        var approval = PendingApproval(AgentRiskLevel.High, "delete_product");

        await CreateRuntime().RespondToApprovalAsync(
            AgentIds.ProductAgent,
            approval.ApprovalId,
            approved: false);

        Assert.Equal(AgentApprovalStatus.Rejected, approval.Status);
        Assert.Equal(ApplicationTestContext.DefaultUserId, approval.DecidedByUserId);
    }

    [Fact]
    public async Task MediumRiskAction_IsStillApprovedByTheRequester()
    {
        var approval = PendingApproval(AgentRiskLevel.Medium, "create_product");

        await CreateRuntime().RespondToApprovalAsync(
            AgentIds.ProductAgent,
            approval.ApprovalId,
            approved: true);

        Assert.Equal(AgentApprovalStatus.Approved, approval.Status);
        Assert.Equal(ApplicationTestContext.DefaultUserId, approval.DecidedByUserId);

        _approvedExecution.Verify(
            x => x.ExecuteAsync(approval, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ---- Allowed roles -----------------------------------------------------

    private static CodeAgentDefinitionProvider ProviderWithRoles(params string[] roles)
    {
        var template = AgentDefinitions.ProductAgent;

        return new CodeAgentDefinitionProvider(
        [
            new AgentDefinition
            {
                AgentId = template.AgentId,
                Name = template.Name,
                Version = template.Version,
                Instructions = template.Instructions,
                Tools = template.Tools,
                AllowedRoles = roles
            }
        ]);
    }

    private void SignInAs(string role)
    {
        // The accessor accepts one context; start from a fresh one.
        _executionContext = new ExecutionContextAccessor();
        _executionContext.Set(new IronForge.Application.Execution.ExecutionContext
        {
            TenantId = ApplicationTestContext.DefaultTenantId,
            Actor = new ExecutionActor
            {
                UserId = ApplicationTestContext.DefaultUserId,
                Role = role
            }
        });
    }

    [Fact]
    public async Task RunAsync_RoleNotAllowedForTheAgent_IsRefusedBeforeAnyModelCall()
    {
        SignInAs("Viewer");

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CreateRuntime(ProviderWithRoles("Admin", "Member")).RunAsync(
                AgentIds.ProductAgent,
                sessionId: null,
                "delete everything"));

        Assert.Contains("Your role cannot use agent", exception.Message);

        _chatClient.Verify(
            x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_AllowedRole_Runs()
    {
        SignInAs("member");

        var session = Session(AgentIds.ProductAgent);

        _conversationService
            .Setup(x => x.CreateSessionAsync(AgentIds.ProductAgent, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var result = await CreateRuntime(ProviderWithRoles("Member")).RunAsync(
            AgentIds.ProductAgent,
            sessionId: null,
            "list my products");

        Assert.Equal(AgentRunStatus.Completed, result.Status);
    }
}
