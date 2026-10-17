using System.Globalization;
using IronForge.Application.Entities;
using IronForge.Application.Gym.Common;
using IronForge.Application.Gym.Notifications;
using IronForge.Application.Persistence;
using IronForge.Shared.Models.Gym;

namespace IronForge.Application.Gym.Billing;

/// <summary>Issues gym invoice numbers atomically in the database.</summary>
public interface IInvoiceNumberGenerator
{
    Task<int> NextAsync(int tenantId, CancellationToken cancellationToken = default);
}

/// <summary>A gym and its settings, loaded together for billing work.</summary>
public sealed record GymInfo(int TenantId, string Name, GymSettings Settings);

public sealed class BillingRunResult
{
    public int InvoicesIssued { get; set; }

    public int InvoicesOverdue { get; set; }

    public int MembersExpired { get; set; }

    public int MembersUnfrozen { get; set; }

    public int RemindersSent { get; set; }
}

/// <summary>
/// The billing rules, without authorization: used by the staff and member
/// services (which authorize first), the daily billing job and the payment
/// webhook. Methods that take tracked entities change them but do not save;
/// the caller saves once so the change, its notifications and its emails
/// commit together.
/// </summary>
public interface IBillingEngine
{
    Task<GymInfo?> GetGymAsync(int tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invoices the member's next unbilled period (from NextBillingDate, or
    /// today for a first invoice) and advances NextBillingDate.
    /// </summary>
    Task<Invoice> IssueNextInvoiceAsync(
        GymInfo gym,
        MemberProfile member,
        MembershipPlan plan,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an unpaid invoice paid in full, extends the member's paid period
    /// and reactivates a Pending or Expired member.
    /// </summary>
    Payment ApplyPayment(
        GymInfo gym,
        Invoice invoice,
        MemberProfile member,
        PaymentMethod method,
        string provider,
        string? providerReference,
        int? recordedByUserId);

    /// <summary>Queues a payment reminder (in-app and email) and records it.</summary>
    void QueueReminder(GymInfo gym, Invoice invoice, MemberProfile member, string kind);

    /// <summary>The daily run for one gym. The caller sets the tenant execution context.</summary>
    Task<BillingRunResult> RunAsync(int tenantId, CancellationToken cancellationToken = default);
}

public sealed class BillingEngine : IBillingEngine
{
    /// <summary>Most periods one member is invoiced for in a single run (catch-up after downtime).</summary>
    private const int MaximumCatchUpPeriods = 3;

    private readonly IRepository<Tenant> _tenants;
    private readonly IRepository<GymSettings> _settings;
    private readonly IRepository<MemberProfile> _members;
    private readonly IRepository<MembershipPlan> _plans;
    private readonly IRepository<Invoice> _invoices;
    private readonly IRepository<Payment> _payments;
    private readonly IRepository<InvoiceReminder> _reminders;
    private readonly IInvoiceNumberGenerator _numbers;
    private readonly INotificationService _notifications;
    private readonly TimeProvider _clock;

    public BillingEngine(
        IRepository<Tenant> tenants,
        IRepository<GymSettings> settings,
        IRepository<MemberProfile> members,
        IRepository<MembershipPlan> plans,
        IRepository<Invoice> invoices,
        IRepository<Payment> payments,
        IRepository<InvoiceReminder> reminders,
        IInvoiceNumberGenerator numbers,
        INotificationService notifications,
        TimeProvider clock)
    {
        _tenants = tenants;
        _settings = settings;
        _members = members;
        _plans = plans;
        _invoices = invoices;
        _payments = payments;
        _reminders = reminders;
        _numbers = numbers;
        _notifications = notifications;
        _clock = clock;
    }

    public async Task<GymInfo?> GetGymAsync(int tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await _tenants.FirstOrDefaultAsync(x => x.Id == tenantId, cancellationToken);
        var settings = await _settings.FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);

        return tenant is null || settings is null
            ? null
            : new GymInfo(tenant.Id, tenant.Name, settings);
    }

    public async Task<Invoice> IssueNextInvoiceAsync(
        GymInfo gym,
        MemberProfile member,
        MembershipPlan plan,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var today = GymCalendar.Today(_clock, gym.Settings.TimeZoneId);

        var periodStart = member.NextBillingDate ?? today;
        var periodEnd = GymCalendar.PeriodEnd(periodStart, plan.BillingInterval);

        // A period that already started (first invoice, catch-up) is due now.
        var dueDate = periodStart < today ? today : periodStart;

        var number = await _numbers.NextAsync(gym.TenantId, cancellationToken);

        var invoice = new Invoice
        {
            TenantId = gym.TenantId,
            Number = GymCalendar.FormatInvoiceNumber(number),
            MemberProfileId = member.Id,
            MembershipPlanId = plan.Id,
            Description = $"{plan.Name} membership, {Format(periodStart)} – {Format(periodEnd)}",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Amount = plan.Price,
            Currency = gym.Settings.Currency,
            IssuedAtUtc = now,
            DueDate = dueDate,
            Status = InvoiceStatus.Open
        };

        _invoices.Add(invoice);

        member.NextBillingDate = periodEnd.AddDays(1);

        _notifications.Queue(
            gym.TenantId,
            [Recipient(member)],
            new NotificationMessage
            {
                Type = NotificationType.Payment,
                Title = $"New invoice {invoice.Number}",
                Body =
                    $"Your invoice for {invoice.Description} is ready.\n\n" +
                    $"Amount: {FormatMoney(invoice.Amount, invoice.Currency)}. Due on {Format(invoice.DueDate)}.\n\n" +
                    "You can pay it in the IronForge app.",
                Link = "/invoices",
                SendEmail = true,
                GymName = gym.Name
            });

        return invoice;
    }

    public Payment ApplyPayment(
        GymInfo gym,
        Invoice invoice,
        MemberProfile member,
        PaymentMethod method,
        string provider,
        string? providerReference,
        int? recordedByUserId)
    {
        if (!invoice.IsUnpaid)
        {
            throw new InvalidOperationException($"Invoice {invoice.Number} is not payable.");
        }

        var now = _clock.GetUtcNow().UtcDateTime;

        invoice.Status = InvoiceStatus.Paid;
        invoice.PaidAtUtc = now;

        var payment = new Payment
        {
            TenantId = gym.TenantId,
            InvoiceId = invoice.Id,
            Invoice = invoice.Id == 0 ? invoice : null,
            MemberProfileId = member.Id,
            Amount = invoice.Amount,
            Currency = invoice.Currency,
            Method = method,
            Provider = provider,
            ProviderReference = providerReference,
            RecordedByUserId = recordedByUserId,
            PaidAtUtc = now
        };

        _payments.Add(payment);

        if (member.PaidThrough is null || member.PaidThrough < invoice.PeriodEnd)
        {
            member.PaidThrough = invoice.PeriodEnd;
        }

        if (member.Status is MemberStatus.Expired or MemberStatus.Pending)
        {
            member.Status = MemberStatus.Active;
            member.ApprovedAtUtc ??= now;
        }

        _notifications.Queue(
            gym.TenantId,
            [Recipient(member)],
            new NotificationMessage
            {
                Type = NotificationType.Payment,
                Title = $"Payment received for {invoice.Number}",
                Body =
                    $"Thank you! We received {FormatMoney(invoice.Amount, invoice.Currency)} " +
                    $"for {invoice.Description}.\n\nYour membership is paid through {Format(member.PaidThrough!.Value)}.",
                Link = "/invoices",
                SendEmail = true,
                GymName = gym.Name
            });

        return payment;
    }

    public void QueueReminder(GymInfo gym, Invoice invoice, MemberProfile member, string kind)
    {
        var today = GymCalendar.Today(_clock, gym.Settings.TimeZoneId);
        var overdueDays = today.DayNumber - invoice.DueDate.DayNumber;

        var when = overdueDays switch
        {
            > 0 => $"was due on {Format(invoice.DueDate)} ({overdueDays} day{(overdueDays == 1 ? "" : "s")} ago)",
            0 => "is due today",
            _ => $"is due on {Format(invoice.DueDate)}"
        };

        _reminders.Add(new InvoiceReminder
        {
            TenantId = gym.TenantId,
            InvoiceId = invoice.Id,
            Kind = kind,
            SentAtUtc = _clock.GetUtcNow().UtcDateTime
        });

        _notifications.Queue(
            gym.TenantId,
            [Recipient(member)],
            new NotificationMessage
            {
                Type = NotificationType.PaymentReminder,
                Title = overdueDays > 0
                    ? $"Payment overdue: {invoice.Number}"
                    : $"Payment reminder: {invoice.Number}",
                Body =
                    $"Hi {FirstName(member.FullName)}, your invoice {invoice.Number} for " +
                    $"{FormatMoney(invoice.Amount, invoice.Currency)} {when}.\n\n" +
                    "You can pay it in the IronForge app, or at the front desk.",
                Link = "/invoices",
                SendEmail = true,
                GymName = gym.Name
            });
    }

    public async Task<BillingRunResult> RunAsync(int tenantId, CancellationToken cancellationToken = default)
    {
        var result = new BillingRunResult();

        var gym = await GetGymAsync(tenantId, cancellationToken);

        if (gym is null)
        {
            return result;
        }

        var settings = gym.Settings;
        var today = GymCalendar.Today(_clock, settings.TimeZoneId);

        // 1. Freezes that ended.
        var unfreezing = await _members.ListTrackedAsync(
            x => x.TenantId == tenantId &&
                 x.Status == MemberStatus.Frozen &&
                 x.FrozenUntil != null &&
                 x.FrozenUntil < today,
            cancellationToken);

        foreach (var member in unfreezing)
        {
            member.Status = MemberStatus.Active;
            member.FrozenUntil = null;
            result.MembersUnfrozen++;
        }

        // 2. Invoices for periods starting within the lead time.
        var horizon = today.AddDays(settings.InvoiceLeadDays);

        var due = await _members.ListTrackedAsync(
            x => x.TenantId == tenantId &&
                 x.Status == MemberStatus.Active &&
                 x.MembershipPlanId != null &&
                 x.NextBillingDate != null &&
                 x.NextBillingDate <= horizon,
            cancellationToken);

        if (due.Count > 0)
        {
            var planIds = due.Select(x => x.MembershipPlanId!.Value).Distinct().ToList();
            var plans = (await _plans.ListAsync(x => planIds.Contains(x.Id), cancellationToken))
                .ToDictionary(x => x.Id);

            foreach (var member in due)
            {
                if (!plans.TryGetValue(member.MembershipPlanId!.Value, out var plan))
                    continue;

                for (var i = 0; i < MaximumCatchUpPeriods && member.NextBillingDate <= horizon; i++)
                {
                    await IssueNextInvoiceAsync(gym, member, plan, cancellationToken);
                    result.InvoicesIssued++;
                }
            }
        }

        // 3. Open invoices past their due date.
        var overdue = await _invoices.ListTrackedAsync(
            x => x.TenantId == tenantId &&
                 x.Status == InvoiceStatus.Open &&
                 x.DueDate < today,
            cancellationToken);

        foreach (var invoice in overdue)
        {
            invoice.Status = InvoiceStatus.Overdue;
            result.InvoicesOverdue++;
        }

        await _invoices.SaveChangesAsync(cancellationToken);

        // 4. Memberships with an invoice overdue past the grace period.
        var expiryCutoff = today.AddDays(-settings.GracePeriodDays);

        var lapsedMemberIds = await _invoices.QueryAsync(
            q => q
                .Where(x => x.TenantId == tenantId &&
                            x.Status == InvoiceStatus.Overdue &&
                            x.DueDate < expiryCutoff)
                .Select(x => x.MemberProfileId)
                .Distinct(),
            cancellationToken);

        if (lapsedMemberIds.Count > 0)
        {
            var lapsed = await _members.ListTrackedAsync(
                x => lapsedMemberIds.Contains(x.Id) && x.Status == MemberStatus.Active,
                cancellationToken);

            foreach (var member in lapsed)
            {
                member.Status = MemberStatus.Expired;
                result.MembersExpired++;

                _notifications.Queue(
                    tenantId,
                    [Recipient(member)],
                    new NotificationMessage
                    {
                        Type = NotificationType.Membership,
                        Title = "Your membership has expired",
                        Body =
                            $"Hi {FirstName(member.FullName)}, your membership expired because an invoice " +
                            $"stayed unpaid for more than {settings.GracePeriodDays} days.\n\n" +
                            "Pay the open invoice in the app to reactivate it straight away.",
                        Link = "/invoices",
                        SendEmail = true,
                        GymName = gym.Name
                    });
            }
        }

        // 5. Reminders, once per invoice and rule.
        result.RemindersSent = await QueueAutomaticRemindersAsync(gym, today, cancellationToken);

        await _invoices.SaveChangesAsync(cancellationToken);

        return result;
    }

    private async Task<int> QueueAutomaticRemindersAsync(
        GymInfo gym,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var settings = gym.Settings;
        var tenantId = gym.TenantId;
        var candidates = new List<(Invoice Invoice, string Kind)>();

        if (settings.ReminderDaysBeforeDue > 0)
        {
            var dueOn = today.AddDays(settings.ReminderDaysBeforeDue);

            var upcoming = await _invoices.ListAsync(
                x => x.TenantId == tenantId && x.Status == InvoiceStatus.Open && x.DueDate == dueOn,
                cancellationToken);

            candidates.AddRange(upcoming.Select(x => (x, $"before:{settings.ReminderDaysBeforeDue}")));
        }

        foreach (var days in settings.ReminderDaysAfterDue.Where(x => x > 0).Distinct())
        {
            var dueOn = today.AddDays(-days);

            var late = await _invoices.ListAsync(
                x => x.TenantId == tenantId && x.Status == InvoiceStatus.Overdue && x.DueDate == dueOn,
                cancellationToken);

            candidates.AddRange(late.Select(x => (x, $"after:{days}")));
        }

        if (candidates.Count == 0)
        {
            return 0;
        }

        var invoiceIds = candidates.Select(x => x.Invoice.Id).Distinct().ToList();

        var sent = (await _reminders.ListAsync(x => invoiceIds.Contains(x.InvoiceId), cancellationToken))
            .Select(x => (x.InvoiceId, x.Kind))
            .ToHashSet();

        var memberIds = candidates.Select(x => x.Invoice.MemberProfileId).Distinct().ToList();

        var members = (await _members.ListAsync(x => memberIds.Contains(x.Id), cancellationToken))
            .ToDictionary(x => x.Id);

        var count = 0;

        foreach (var (invoice, kind) in candidates)
        {
            if (sent.Contains((invoice.Id, kind)) ||
                !members.TryGetValue(invoice.MemberProfileId, out var member) ||
                member.Status == MemberStatus.Cancelled ||
                member.AnonymizedAtUtc != null)
            {
                continue;
            }

            QueueReminder(gym, invoice, member, kind);
            count++;
        }

        return count;
    }

    public static NotificationRecipient Recipient(MemberProfile member) =>
        new(member.UserId, member.AnonymizedAtUtc is null ? member.Email : null, member.FullName);

    public static string FormatMoney(decimal amount, string currency) =>
        $"{amount.ToString("N2", CultureInfo.InvariantCulture)} {currency}";

    public static string Format(DateOnly date) =>
        date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    private static string FirstName(string fullName) =>
        fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? fullName;
}
