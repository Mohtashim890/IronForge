using System.ComponentModel;
using IronForge.Application.Agents.Governance;
using IronForge.Application.Agents.Tools;
using IronForge.Application.Gym.Billing;
using IronForge.Application.Gym.Members;
using IronForge.Application.Gym.Notifications;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Gym;
using IronForge.Shared.Subscriptions;

namespace IronForge.Application.Gym.AgentTools;

// Tools of the Gym Admin Agent. They act with the signed-in staff member's
// authority (user ∩ agent): a Trainer using the agent cannot see billing.
// Actions that reach members or money require human approval.

public sealed class SearchMembersAgentTool : AgentTool
{
    private readonly IMemberService _members;

    public SearchMembersAgentTool(IMemberService members) => _members = members;

    public override AgentToolDescriptor Descriptor { get; } = new()
    {
        Name = "search_members",
        Description =
            "Searches gym members by name, email or phone, optionally by status or only those with " +
            "overdue invoices. Returns id, status, plan, paid-through date, outstanding amount and last check-in.",
        RequiredPermission = Permissions.GymMembersRead,
        Module = PlatformModules.GymCore
    };

    protected override Delegate Handler => ExecuteAsync;

    private async Task<object> ExecuteAsync(
        [Description("Optional part of a name, email or phone number.")]
        string? search = null,
        [Description("Optional status: Pending, Active, Frozen, Expired or Cancelled.")]
        string? status = null,
        [Description("True for members with at least one overdue invoice.")]
        bool overdueOnly = false,
        [Description("Maximum results, 1 to 50. Default 20.")]
        int limit = 20) =>
        GymToolResult.From(await _members.ListAsync(new MemberQuery
        {
            Search = search,
            Status = Enum.TryParse<MemberStatus>(status, ignoreCase: true, out var parsed) ? parsed : null,
            OverdueOnly = overdueOnly,
            PageSize = Math.Clamp(limit, 1, 50)
        }));
}

public sealed class GetMemberDetailsAgentTool : AgentTool
{
    private readonly IMemberService _members;

    public GetMemberDetailsAgentTool(IMemberService members) => _members = members;

    public override AgentToolDescriptor Descriptor { get; } = new()
    {
        Name = "get_member_details",
        Description = "Gets one member's profile, recent invoices, recent check-ins, routines and goals.",
        RequiredPermission = Permissions.GymMembersRead,
        Module = PlatformModules.GymCore
    };

    protected override Delegate Handler => ExecuteAsync;

    private async Task<object> ExecuteAsync(
        [Description("Member id from search_members.")]
        int memberId) =>
        GymToolResult.From(await _members.GetAsync(memberId));
}

public sealed class GetDashboardSummaryAgentTool : AgentTool
{
    private readonly IGymDashboardService _dashboard;

    public GetDashboardSummaryAgentTool(IGymDashboardService dashboard) => _dashboard = dashboard;

    public override AgentToolDescriptor Descriptor { get; } = new()
    {
        Name = "get_dashboard_summary",
        Description =
            "Gets the gym's key numbers: members by status, new members this month, revenue this and " +
            "last month, overdue amount, check-ins today and this week, memberships expiring soon.",
        RequiredPermission = Permissions.GymMembersRead,
        Module = PlatformModules.GymCore
    };

    protected override Delegate Handler => ExecuteAsync;

    private async Task<object> ExecuteAsync() =>
        GymToolResult.From(await _dashboard.GetAsync());
}

public sealed class GetAttendanceSummaryAgentTool : AgentTool
{
    private readonly ICheckInService _checkIns;

    public GetAttendanceSummaryAgentTool(ICheckInService checkIns) => _checkIns = checkIns;

    public override AgentToolDescriptor Descriptor { get; } = new()
    {
        Name = "get_attendance_summary",
        Description =
            "Summarises check-ins over recent days: totals, unique members, busiest day and hour, " +
            "daily counts, and active members who did not come in at all.",
        RequiredPermission = Permissions.GymMembersRead,
        Module = PlatformModules.GymCore
    };

    protected override Delegate Handler => ExecuteAsync;

    private async Task<object> ExecuteAsync(
        [Description("Days to look back, 1 to 90. Default 30.")]
        int days = 30) =>
        GymToolResult.From(await _checkIns.GetAttendanceAsync(days));
}

public sealed class ListExpiringMembershipsAgentTool : AgentTool
{
    private readonly IGymDashboardService _dashboard;

    public ListExpiringMembershipsAgentTool(IGymDashboardService dashboard) => _dashboard = dashboard;

    public override AgentToolDescriptor Descriptor { get; } = new()
    {
        Name = "list_expiring_memberships",
        Description = "Lists active members whose paid period ends within the next days.",
        RequiredPermission = Permissions.GymMembersRead,
        Module = PlatformModules.GymCore
    };

    protected override Delegate Handler => ExecuteAsync;

    private async Task<object> ExecuteAsync(
        [Description("Days ahead, 1 to 60. Default 7.")]
        int days = 7) =>
        GymToolResult.From(await _dashboard.ListExpiringAsync(days));
}

public sealed class FreezeMembershipAgentTool : AgentTool
{
    private readonly IMemberService _members;

    public FreezeMembershipAgentTool(IMemberService members) => _members = members;

    public override AgentToolDescriptor Descriptor { get; } = new()
    {
        Name = "freeze_membership",
        Description =
            "Freezes an active membership until a date: billing pauses and the paid period is " +
            "extended by the freeze. Requires approval.",
        RequiredPermission = Permissions.GymMembersManage,
        Module = PlatformModules.GymCore,
        RiskLevel = AgentRiskLevel.Medium,
        RequiresApproval = true
    };

    protected override Delegate Handler => ExecuteAsync;

    private async Task<object> ExecuteAsync(
        [Description("Member id.")]
        int memberId,
        [Description("Last day of the freeze (yyyy-MM-dd), at most 180 days ahead.")]
        DateOnly until,
        [Description("Reason, e.g. 'travel' or 'injury'.")]
        string? reason = null) =>
        GymToolResult.From(await _members.FreezeAsync(memberId, new FreezeMemberRequest { Until = until, Reason = reason }));
}

public sealed class ListOverdueInvoicesAgentTool : AgentTool
{
    private readonly IBillingService _billing;

    public ListOverdueInvoicesAgentTool(IBillingService billing) => _billing = billing;

    public override AgentToolDescriptor Descriptor { get; } = new()
    {
        Name = "list_overdue_invoices",
        Description = "Lists overdue invoices with member, amount, due date and days overdue.",
        RequiredPermission = Permissions.GymBillingRead,
        Module = PlatformModules.GymBilling
    };

    protected override Delegate Handler => ExecuteAsync;

    private async Task<object> ExecuteAsync(
        [Description("Maximum results, 1 to 100. Default 50.")]
        int limit = 50)
    {
        var result = await _billing.ListInvoicesAsync(new InvoiceQuery
        {
            Status = InvoiceStatus.Overdue,
            PageSize = Math.Clamp(limit, 1, 100)
        });

        return result.Success
            ? GymToolResult.Ok(new
            {
                result.Data!.TotalCount,
                TotalAmount = result.Data.Items.Sum(x => x.Amount),
                Invoices = result.Data.Items.Select(x => new
                {
                    InvoiceId = x.Id,
                    x.Number,
                    x.MemberId,
                    x.MemberName,
                    x.Amount,
                    x.Currency,
                    x.DueDate,
                    x.DaysOverdue
                })
            })
            : GymToolResult.From(result);
    }
}

public sealed class GetRevenueSummaryAgentTool : AgentTool
{
    private readonly IBillingService _billing;

    public GetRevenueSummaryAgentTool(IBillingService billing) => _billing = billing;

    public override AgentToolDescriptor Descriptor { get; } = new()
    {
        Name = "get_revenue_summary",
        Description = "Gets payments collected per month for recent months.",
        RequiredPermission = Permissions.GymBillingRead,
        Module = PlatformModules.GymBilling
    };

    protected override Delegate Handler => ExecuteAsync;

    private async Task<object> ExecuteAsync(
        [Description("Number of months including the current one, 1 to 24. Default 6.")]
        int months = 6) =>
        GymToolResult.From(await _billing.GetRevenueByMonthAsync(months));
}

public sealed class SendPaymentReminderAgentTool : AgentTool
{
    private readonly IBillingService _billing;

    public SendPaymentReminderAgentTool(IBillingService billing) => _billing = billing;

    public override AgentToolDescriptor Descriptor { get; } = new()
    {
        Name = "send_payment_reminder",
        Description = "Sends a payment reminder (in-app and email) for one unpaid invoice. Requires approval.",
        RequiredPermission = Permissions.GymBillingManage,
        Module = PlatformModules.GymBilling,
        RiskLevel = AgentRiskLevel.Medium,
        RequiresApproval = true
    };

    protected override Delegate Handler => ExecuteAsync;

    private async Task<object> ExecuteAsync(
        [Description("Invoice id from list_overdue_invoices.")]
        int invoiceId) =>
        GymToolResult.From(await _billing.SendReminderAsync(invoiceId));
}

public sealed class RecordCashPaymentAgentTool : AgentTool
{
    private readonly IBillingService _billing;

    public RecordCashPaymentAgentTool(IBillingService billing) => _billing = billing;

    public override AgentToolDescriptor Descriptor { get; } = new()
    {
        Name = "record_cash_payment",
        Description =
            "Records that a member paid an invoice in cash at the desk. The amount must equal the " +
            "invoice total. High risk: needs approval by a second admin.",
        RequiredPermission = Permissions.GymBillingManage,
        Module = PlatformModules.GymBilling,
        RiskLevel = AgentRiskLevel.High,
        RequiresApproval = true
    };

    protected override Delegate Handler => ExecuteAsync;

    private async Task<object> ExecuteAsync(
        [Description("Invoice id.")]
        int invoiceId,
        [Description("Amount received; must equal the invoice total.")]
        decimal amount,
        [Description("Optional receipt number or note.")]
        string? reference = null) =>
        GymToolResult.From(await _billing.RecordPaymentAsync(
            invoiceId,
            new RecordPaymentRequest { Amount = amount, Method = PaymentMethod.Cash, Reference = reference }));
}

public sealed class SendAnnouncementAgentTool : AgentTool
{
    private readonly IAnnouncementService _announcements;

    public SendAnnouncementAgentTool(IAnnouncementService announcements) => _announcements = announcements;

    public override AgentToolDescriptor Descriptor { get; } = new()
    {
        Name = "send_announcement",
        Description =
            "Sends an announcement to members in the app and by email. Show the user the exact title " +
            "and text first. High risk: needs approval by a second admin.",
        RequiredPermission = Permissions.GymNotificationsSend,
        Module = PlatformModules.GymEngagement,
        RiskLevel = AgentRiskLevel.High,
        RequiresApproval = true
    };

    protected override Delegate Handler => ExecuteAsync;

    private async Task<object> ExecuteAsync(
        [Description("Short title.")]
        string title,
        [Description("The message. Plain text; blank lines separate paragraphs.")]
        string body,
        [Description("All, Active, Overdue, or Plan (then give membershipPlanId). Default All.")]
        string audience = "All",
        [Description("Plan id when audience is Plan.")]
        int? membershipPlanId = null) =>
        GymToolResult.From(await _announcements.SendAsync(new CreateAnnouncementRequest
        {
            Title = title,
            Body = body,
            Audience = Enum.TryParse<AnnouncementAudience>(audience, ignoreCase: true, out var parsed)
                ? parsed
                : AnnouncementAudience.All,
            MembershipPlanId = membershipPlanId,
            SendEmail = true
        }));
}
