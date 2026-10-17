using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Auth.Services;
using IronForge.Application.Commons;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.Persistence;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Agents;

namespace IronForge.Application.Agents.Approvals;

/// <summary>
/// The second pair of eyes for High/Critical-risk agent actions
/// (<see cref="ApprovalRules"/>). Approvers decide here; deciding does not
/// execute anything. The requester then runs the approved action from their
/// own context, so it executes under the requester's authority.
/// </summary>
public interface IApprovalQueueService
{
    Task<ServiceResult<ApprovalQueueDto>> GetAsync(
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ApprovalQueueItemDto>> DecideAsync(
        string approvalId,
        bool approved,
        string? reason,
        CancellationToken cancellationToken = default);
}

public sealed class ApprovalQueueService : IApprovalQueueService
{
    private readonly IRepository<AgentApproval> _approvals;
    private readonly IRepository<User> _users;
    private readonly IRepository<Agent> _agents;
    private readonly IUserAuthorizationService _authorization;
    private readonly IExecutionContextAccessor _executionContext;
    private readonly IAuditService _auditService;

    public ApprovalQueueService(
        IRepository<AgentApproval> approvals,
        IRepository<User> users,
        IRepository<Agent> agents,
        IUserAuthorizationService authorization,
        IExecutionContextAccessor executionContext,
        IAuditService auditService)
    {
        _approvals = approvals;
        _users = users;
        _agents = agents;
        _authorization = authorization;
        _executionContext = executionContext;
        _auditService = auditService;
    }

    public async Task<ServiceResult<ApprovalQueueDto>> GetAsync(
        CancellationToken cancellationToken = default)
    {
        if (CurrentUser() is not { } userId)
        {
            return ServiceResult<ApprovalQueueDto>.Fail("A tenant must be selected.", 403);
        }

        var now = DateTime.UtcNow;

        // Tenant-filtered. Only open High/Critical requests use the queue.
        var open = await _approvals.ListAsync(
            x =>
                (x.Status == AgentApprovalStatus.Pending ||
                 x.Status == AgentApprovalStatus.Approved) &&
                x.ExpiresAtUtc > now &&
                x.RiskLevel >= Governance.AgentRiskLevel.High,
            cancellationToken);

        var canDecide = _authorization.HasPermission(Permissions.ApprovalsDecide);

        var awaiting = canDecide
            ? open.Where(x => x.Status == AgentApprovalStatus.Pending && x.UserId != userId).ToList()
            : [];

        // An Approved request is shown to its requester only when a second
        // approver approved it (i.e. it is ready to run).
        var mine = open
            .Where(x =>
                x.UserId == userId &&
                (x.Status == AgentApprovalStatus.Pending || ApprovalRules.HasSecondApproval(x)))
            .ToList();

        var items = await MapAsync(awaiting.Concat(mine).ToList(), userId, cancellationToken);

        return ServiceResult<ApprovalQueueDto>.Ok(new ApprovalQueueDto
        {
            AwaitingYourDecision = items.Where(x => awaiting.Any(a => a.ApprovalId == x.ApprovalId)).ToList(),
            YourRequests = items.Where(x => mine.Any(m => m.ApprovalId == x.ApprovalId)).ToList()
        });
    }

    public async Task<ServiceResult<ApprovalQueueItemDto>> DecideAsync(
        string approvalId,
        bool approved,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        if (CurrentUser() is not { } userId)
        {
            return ServiceResult<ApprovalQueueItemDto>.Fail("A tenant must be selected.", 403);
        }

        if (!_authorization.HasPermission(Permissions.ApprovalsDecide))
        {
            return ServiceResult<ApprovalQueueItemDto>.Fail(
                "You are not authorized to decide agent approvals.",
                403);
        }

        var approval = await _approvals.FirstOrDefaultTrackedAsync(
            x => x.ApprovalId == approvalId,
            cancellationToken);

        if (approval is null)
        {
            return ServiceResult<ApprovalQueueItemDto>.Fail("Approval was not found.", 404);
        }

        if (!ApprovalRules.RequiresSecondApprover(approval.RiskLevel))
        {
            return ServiceResult<ApprovalQueueItemDto>.Fail(
                "Only High and Critical-risk actions are decided in the queue; the requester approves the others.",
                400);
        }

        if (approval.UserId == userId)
        {
            return ServiceResult<ApprovalQueueItemDto>.Fail(
                "You cannot approve your own request.",
                403);
        }

        if (approval.Status != AgentApprovalStatus.Pending)
        {
            return ServiceResult<ApprovalQueueItemDto>.Fail(
                $"This request has already been decided ({approval.Status}).",
                409);
        }

        if (approval.ExpiresAtUtc <= DateTime.UtcNow)
        {
            return ServiceResult<ApprovalQueueItemDto>.Fail("This request has expired.", 409);
        }

        approval.Status = approved
            ? AgentApprovalStatus.Approved
            : AgentApprovalStatus.Rejected;
        approval.DecisionReason = reason?.Trim();
        approval.DecidedAtUtc = DateTime.UtcNow;
        approval.DecidedByUserId = userId;

        await _approvals.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(
            AuditCategory.Approval,
            approved ? "ApprovalGrantedBySecondApprover" : "ApprovalRejectedBySecondApprover",
            AuditOutcome.Succeeded,
            actorType: AuditActorType.User,
            resourceType: "AgentApproval",
            resourceId: approval.ApprovalId,
            reason: $"{approval.ToolName} requested by user {approval.UserId}.",
            cancellationToken: cancellationToken);

        var item = (await MapAsync([approval], userId, cancellationToken)).Single();

        return ServiceResult<ApprovalQueueItemDto>.Ok(item);
    }

    private int? CurrentUser()
    {
        var context = _executionContext.Current;

        return context?.TenantId is null ? null : context.Actor.UserId;
    }

    private async Task<List<ApprovalQueueItemDto>> MapAsync(
        List<AgentApproval> approvals,
        int currentUserId,
        CancellationToken cancellationToken)
    {
        if (approvals.Count == 0)
            return [];

        var userIds = approvals
            .Select(x => x.UserId)
            .Concat(approvals.Where(x => x.DecidedByUserId.HasValue).Select(x => x.DecidedByUserId!.Value))
            .Distinct()
            .ToList();

        var users = (await _users.ListAsync(x => userIds.Contains(x.Id), cancellationToken))
            .ToDictionary(x => x.Id, x => x.Username);

        var agentIds = approvals.Select(x => x.AgentId).Distinct().ToList();

        var agents = (await _agents.ListAsync(x => agentIds.Contains(x.AgentId), cancellationToken))
            .ToDictionary(x => x.AgentId, x => x.Name);

        var now = DateTime.UtcNow;

        return approvals
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new ApprovalQueueItemDto
            {
                ApprovalId = x.ApprovalId,
                AgentId = x.AgentId,
                AgentName = agents.GetValueOrDefault(x.AgentId, x.AgentId),
                ToolName = x.ToolName,
                ArgumentsJson = x.ArgumentsJson,
                RiskLevel = x.RiskLevel.ToString(),
                Status = x.Status.ToString(),
                RequestedBy = users.GetValueOrDefault(x.UserId, $"user {x.UserId}"),
                DecidedBy = x.DecidedByUserId is { } decidedBy
                    ? users.GetValueOrDefault(decidedBy, $"user {decidedBy}")
                    : null,
                CreatedAtUtc = x.CreatedAtUtc,
                ExpiresAtUtc = x.ExpiresAtUtc,
                CanRun =
                    x.UserId == currentUserId &&
                    ApprovalRules.HasSecondApproval(x) &&
                    x.ExpiresAtUtc > now
            })
            .ToList();
    }
}
