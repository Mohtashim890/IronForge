using IronForge.Application.Entities;
using IronForge.Application.Execution;

namespace IronForge.Application.Agents.Approvals;

public sealed class ApprovalExecutionAuthorizationService
    : IApprovalExecutionAuthorizationService
{
    private readonly IExecutionContextAccessor _executionContext;

    public ApprovalExecutionAuthorizationService(
        IExecutionContextAccessor executionContext)
    {
        _executionContext = executionContext;
    }

    public Task<ApprovalExecutionAuthorizationResult> AuthorizeAsync(
        AgentApproval approval,
        CancellationToken cancellationToken = default)
    {
        var context =
            _executionContext.Current;

        if (context is null)
        {
            return Task.FromResult(
                Deny("Execution context has not been established."));
        }

        var currentUserId =
            context.Actor.UserId;

        if (!currentUserId.HasValue)
        {
            return Task.FromResult(
                Deny("No authenticated user is available."));
        }

        if (approval.UserId != currentUserId.Value)
        {
            return Task.FromResult(
                Deny("The approval does not belong to the current user."));
        }

        if (approval.TenantId != context.TenantId)
        {
            return Task.FromResult(
                Deny("The approval does not belong to the current tenant."));
        }

        var agent =
            context.Actor.Agent;

        if (agent is null)
        {
            return Task.FromResult(
                Deny("No agent identity is available."));
        }

        if (!string.Equals(
                approval.AgentId,
                agent.AgentId,
                StringComparison.Ordinal))
        {
            return Task.FromResult(
                Deny("The approval does not belong to the current agent."));
        }

        if (approval.Status != AgentApprovalStatus.Approved)
        {
            return Task.FromResult(
                Deny(
                    $"Approval is not executable because its status is " +
                    $"{approval.Status}."));
        }

        if (approval.ExpiresAtUtc <= DateTime.UtcNow)
        {
            return Task.FromResult(
                Deny("The approval has expired."));
        }

        return Task.FromResult(
            new ApprovalExecutionAuthorizationResult
            {
                Decision = ApprovalExecutionDecision.Allow,
                Reason = "Approval is valid for execution."
            });
    }

    private static ApprovalExecutionAuthorizationResult Deny(
        string reason)
    {
        return new ApprovalExecutionAuthorizationResult
        {
            Decision = ApprovalExecutionDecision.Deny,
            Reason = reason
        };
    }
}