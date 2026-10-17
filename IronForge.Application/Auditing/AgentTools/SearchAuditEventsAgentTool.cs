using IronForge.Shared.Subscriptions;
using System.ComponentModel;
using IronForge.Application.Agents.Governance;
using IronForge.Application.Agents.Tools;
using IronForge.Application.Auditing.DTOs;
using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Execution;
using IronForge.Shared.Authorization;

namespace IronForge.Application.Auditing.AgentTools;

/// <summary>
/// Read-only access to the tenant's audit trail for agents.
///
/// Governance checks the agent's audit.read permission; this tool also
/// checks the user's, because AuditQueryService itself does not. The agent's
/// effective access is therefore user ∩ agent, like the gym tools.
/// </summary>
public sealed class SearchAuditEventsAgentTool : AgentTool
{
    private const int MaximumDays = 90;
    private const int MaximumResults = 50;

    private readonly IAuditQueryService _auditQueryService;
    private readonly IExecutionContextAccessor _executionContext;

    public SearchAuditEventsAgentTool(
        IAuditQueryService auditQueryService,
        IExecutionContextAccessor executionContext)
    {
        _auditQueryService = auditQueryService;
        _executionContext = executionContext;
    }

    public override AgentToolDescriptor Descriptor { get; } =
        new()
        {
            Name = "search_audit_events",
            Description =
                "Searches the organisation's audit trail (newest first): sign-ins, " +
                "authorization decisions, membership and billing events, announcements, agent runs and approvals.",
            RequiredPermission = Permissions.AuditRead,
            Module = PlatformModules.Audit,
            RiskLevel = AgentRiskLevel.Low
        };

    protected override Delegate Handler => ExecuteAsync;

    private async Task<object> ExecuteAsync(
        [Description("Optional category: Authentication, Authorization, Agent, Approval, Tool, Workflow, Delegation, Tenant, Membership, Billing, Coaching or Notification.")]
        string? category = null,
        [Description("Optional outcome: Succeeded, Failed or Denied.")]
        string? outcome = null,
        [Description("How many days back to search, 1 to 90. Default 7.")]
        int days = 7,
        [Description("Maximum number of events to return, 1 to 50. Default 20.")]
        int limit = 20)
    {
        var permissions =
            _executionContext.Current?.Actor.UserPermissions
            ?? new HashSet<string>();

        if (!permissions.Contains(Permissions.AuditRead))
        {
            return Failure("You do not have permission to view audit events.");
        }

        AuditCategory? categoryFilter = null;

        if (!string.IsNullOrWhiteSpace(category))
        {
            if (!Enum.TryParse<AuditCategory>(category, ignoreCase: true, out var parsed) ||
                !Enum.IsDefined(parsed))
            {
                return Failure($"Unknown category '{category}'.");
            }

            categoryFilter = parsed;
        }

        AuditOutcome? outcomeFilter = null;

        if (!string.IsNullOrWhiteSpace(outcome))
        {
            if (!Enum.TryParse<AuditOutcome>(outcome, ignoreCase: true, out var parsed) ||
                !Enum.IsDefined(parsed))
            {
                return Failure($"Unknown outcome '{outcome}'.");
            }

            outcomeFilter = parsed;
        }

        var result =
            await _auditQueryService.QueryAsync(
                new AuditQueryRequest
                {
                    Category = categoryFilter,
                    Outcome = outcomeFilter,
                    FromUtc = DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, MaximumDays)),
                    Page = 1,
                    PageSize = Math.Clamp(limit, 1, MaximumResults)
                });

        if (!result.Success || result.Data is null)
        {
            return Failure(result.ErrorMessage ?? "Audit events could not be retrieved.");
        }

        return new
        {
            Success = true,
            TotalMatching = result.Data.TotalCount,
            Events = result.Data.Items.Select(x => new
            {
                x.OccurredAtUtc,
                Category = x.Category.ToString(),
                x.Action,
                Outcome = x.Outcome.ToString(),
                ActorType = x.ActorType.ToString(),
                x.UserId,
                x.AgentId,
                x.ResourceType,
                x.ResourceId,
                x.Reason
            }),
            Error = (string?)null
        };
    }

    private static object Failure(string error) =>
        new
        {
            Success = false,
            TotalMatching = 0,
            Events = Array.Empty<object>(),
            Error = error
        };
}
