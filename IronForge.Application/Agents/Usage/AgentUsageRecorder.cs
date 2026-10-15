using IronForge.Application.Agents.ModelProfiles;
using IronForge.Application.Agents.Runtime;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.Observablity.Metrics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace IronForge.Application.Agents.Usage;

public interface IAgentUsageRecorder
{
    Task RecordAsync(
        AgentDefinition definition,
        AgentModelProfile profile,
        string operation,
        Guid? sessionId,
        UsageDetails? usage,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Records the token usage of one model call: a durable row (for quotas and
/// reporting) and a metric (for dashboards). Recording failures are logged
/// and never fail the user's request, which has already succeeded.
/// </summary>
public sealed class AgentUsageRecorder : IAgentUsageRecorder
{
    private readonly IAgentUsageStore _store;
    private readonly IExecutionContextAccessor _executionContext;
    private readonly ILogger<AgentUsageRecorder> _logger;

    public AgentUsageRecorder(
        IAgentUsageStore store,
        IExecutionContextAccessor executionContext,
        ILogger<AgentUsageRecorder> logger)
    {
        _store = store;
        _executionContext = executionContext;
        _logger = logger;
    }

    public async Task RecordAsync(
        AgentDefinition definition,
        AgentModelProfile profile,
        string operation,
        Guid? sessionId,
        UsageDetails? usage,
        CancellationToken cancellationToken = default)
    {
        var inputTokens = usage?.InputTokenCount ?? 0;
        var outputTokens = usage?.OutputTokenCount ?? 0;
        var totalTokens = usage?.TotalTokenCount ?? inputTokens + outputTokens;

        var agentTag = IronForgeMetrics.AgentTag(definition.AgentId);
        var profileTag = IronForgeMetrics.ModelProfileTag(profile.Name);

        IronForgeMetrics.AgentTokens.Add(
            inputTokens, agentTag, profileTag, IronForgeMetrics.TokenTypeTag("input"));
        IronForgeMetrics.AgentTokens.Add(
            outputTokens, agentTag, profileTag, IronForgeMetrics.TokenTypeTag("output"));

        if (usage is null)
        {
            _logger.LogWarning(
                "The model did not report token usage for agent {AgentId} " +
                "(profile {ModelProfile}); quota accounting is incomplete for this call.",
                definition.AgentId,
                profile.Name);
        }

        try
        {
            var context = _executionContext.Current;

            await _store.AddAsync(
                new AgentUsageRecord
                {
                    TenantId = context?.TenantId
                        ?? throw new InvalidOperationException("A tenant is required."),
                    UserId = context.Actor.UserId
                        ?? throw new InvalidOperationException("A user is required."),
                    AgentId = definition.AgentId,
                    SessionId = sessionId,
                    Operation = operation,
                    ModelProfile = profile.Name,
                    Model = profile.Model,
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens,
                    TotalTokens = totalTokens,
                    CreatedAtUtc = DateTime.UtcNow
                },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Failed to record token usage for agent {AgentId} in session {SessionId}.",
                definition.AgentId,
                sessionId);
        }
    }
}
