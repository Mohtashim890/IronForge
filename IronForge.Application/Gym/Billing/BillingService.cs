using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Commons;
using IronForge.Application.Entities;
using IronForge.Application.Gym.Common;
using IronForge.Application.Persistence;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Gym;

namespace IronForge.Application.Gym.Billing;

public interface IBillingService
{
    Task<ServiceResult<PagedResult<InvoiceDto>>> ListInvoicesAsync(
        InvoiceQuery query,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<InvoiceDto>> GetInvoiceAsync(
        int invoiceId,
        CancellationToken cancellationToken = default);

    /// <summary>Records a payment taken at the desk; it must cover the invoice in full.</summary>
    Task<ServiceResult<PaymentDto>> RecordPaymentAsync(
        int invoiceId,
        RecordPaymentRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<InvoiceDto>> VoidInvoiceAsync(
        int invoiceId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<SendReminderResultDto>> SendReminderAsync(
        int invoiceId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<PagedResult<PaymentDto>>> ListPaymentsAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<List<RevenuePointDto>>> GetRevenueByMonthAsync(
        int months,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<List<InvoiceDto>>> ListMyInvoicesAsync(
        CancellationToken cancellationToken = default);

    Task<ServiceResult<List<PaymentDto>>> ListMyPaymentsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Starts a card checkout for one of the member's own unpaid invoices.</summary>
    Task<ServiceResult<CheckoutSessionDto>> StartMyCheckoutAsync(
        int invoiceId,
        CancellationToken cancellationToken = default);
}

public sealed class BillingService : IBillingService
{
    private readonly IGymAccessService _access;
    private readonly IRepository<Invoice> _invoices;
    private readonly IRepository<Payment> _payments;
    private readonly IRepository<MemberProfile> _members;
    private readonly IBillingEngine _engine;
    private readonly IPaymentGateway _gateway;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;

    public BillingService(
        IGymAccessService access,
        IRepository<Invoice> invoices,
        IRepository<Payment> payments,
        IRepository<MemberProfile> members,
        IBillingEngine engine,
        IPaymentGateway gateway,
        IAuditService audit,
        TimeProvider clock)
    {
        _access = access;
        _invoices = invoices;
        _payments = payments;
        _members = members;
        _engine = engine;
        _gateway = gateway;
        _audit = audit;
        _clock = clock;
    }

    public async Task<ServiceResult<PagedResult<InvoiceDto>>> ListInvoicesAsync(
        InvoiceQuery query,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymBillingRead, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<PagedResult<InvoiceDto>>(authorization);
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var search = query.Search?.Trim().ToLower();

        IQueryable<Invoice> Filter(IQueryable<Invoice> q)
        {
            if (query.Status.HasValue)
                q = q.Where(x => x.Status == query.Status.Value);
            if (query.MemberId.HasValue)
                q = q.Where(x => x.MemberProfileId == query.MemberId.Value);
            if (query.DueFrom.HasValue)
                q = q.Where(x => x.DueDate >= query.DueFrom.Value);
            if (query.DueTo.HasValue)
                q = q.Where(x => x.DueDate <= query.DueTo.Value);
            if (!string.IsNullOrEmpty(search))
                q = q.Where(x =>
                    x.Number.ToLower().Contains(search) ||
                    x.MemberProfile!.FullName.ToLower().Contains(search) ||
                    x.MemberProfile.Email.Contains(search));
            return q;
        }

        var total = await _invoices.QueryCountAsync(Filter, cancellationToken);

        var items = await _invoices.QueryAsync(
            q => Filter(q)
                .OrderByDescending(x => x.DueDate)
                .ThenByDescending(x => x.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(ProjectInvoice),
            cancellationToken);

        await FillOverdueDaysAsync(items, cancellationToken);

        return ServiceResult<PagedResult<InvoiceDto>>.Ok(new PagedResult<InvoiceDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<ServiceResult<InvoiceDto>> GetInvoiceAsync(
        int invoiceId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymBillingRead, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<InvoiceDto>(authorization);
        }

        var invoice = (await _invoices.QueryAsync(
            q => q.Where(x => x.Id == invoiceId).Select(ProjectInvoice),
            cancellationToken)).FirstOrDefault();

        if (invoice is null)
        {
            return GymResults.NotFound<InvoiceDto>("Invoice");
        }

        await FillOverdueDaysAsync([invoice], cancellationToken);

        return ServiceResult<InvoiceDto>.Ok(invoice);
    }

    public async Task<ServiceResult<PaymentDto>> RecordPaymentAsync(
        int invoiceId,
        RecordPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymBillingManage, cancellationToken);

        if (!authorization.Allowed)
        {
            await _audit.RecordAsync(
                AuditCategory.Billing,
                "PaymentRecordDenied",
                AuditOutcome.Denied,
                resourceType: "Invoice",
                resourceId: invoiceId.ToString(),
                reason: authorization.Reason,
                cancellationToken: cancellationToken);

            return GymResults.Forbidden<PaymentDto>(authorization);
        }

        if (request.Method == PaymentMethod.Card)
        {
            return GymResults.Invalid<PaymentDto>(
                "Card payments are taken through checkout. Record cash or bank transfers here.");
        }

        var invoice = await _invoices.FirstOrDefaultTrackedAsync(x => x.Id == invoiceId, cancellationToken);

        if (invoice is null)
        {
            return GymResults.NotFound<PaymentDto>("Invoice");
        }

        if (!invoice.IsUnpaid)
        {
            return GymResults.Conflict<PaymentDto>($"Invoice {invoice.Number} is {invoice.Status}.");
        }

        if (request.Amount != invoice.Amount)
        {
            return GymResults.Invalid<PaymentDto>(
                $"The amount must equal the invoice total of {BillingEngine.FormatMoney(invoice.Amount, invoice.Currency)}. Partial payments are not supported.");
        }

        var member = await _members.FirstOrDefaultTrackedAsync(
            x => x.Id == invoice.MemberProfileId,
            cancellationToken);

        var gym = await _engine.GetGymAsync(invoice.TenantId, cancellationToken);

        if (member is null || gym is null)
        {
            return GymResults.NotFound<PaymentDto>("Member");
        }

        var payment = _engine.ApplyPayment(
            gym,
            invoice,
            member,
            request.Method,
            "manual",
            string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim(),
            _access.UserId);

        try
        {
            await _invoices.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return GymResults.Conflict<PaymentDto>(
                $"Invoice {invoice.Number} was just updated (perhaps paid online). Refresh and try again.");
        }

        await _audit.RecordAsync(
            AuditCategory.Billing,
            "PaymentRecorded",
            AuditOutcome.Succeeded,
            resourceType: "Invoice",
            resourceId: invoice.Id.ToString(),
            reason: $"{invoice.Number}: {BillingEngine.FormatMoney(payment.Amount, payment.Currency)} by {payment.Method}.",
            cancellationToken: cancellationToken);

        return ServiceResult<PaymentDto>.Ok(new PaymentDto
        {
            Id = payment.Id,
            InvoiceId = invoice.Id,
            InvoiceNumber = invoice.Number,
            MemberId = member.Id,
            MemberName = member.FullName,
            Amount = payment.Amount,
            Currency = payment.Currency,
            Method = payment.Method,
            Provider = payment.Provider,
            PaidAtUtc = payment.PaidAtUtc
        });
    }

    public async Task<ServiceResult<InvoiceDto>> VoidInvoiceAsync(
        int invoiceId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymBillingManage, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<InvoiceDto>(authorization);
        }

        var invoice = await _invoices.FirstOrDefaultTrackedAsync(x => x.Id == invoiceId, cancellationToken);

        if (invoice is null)
        {
            return GymResults.NotFound<InvoiceDto>("Invoice");
        }

        if (!invoice.IsUnpaid)
        {
            return GymResults.Conflict<InvoiceDto>($"Only unpaid invoices can be voided; {invoice.Number} is {invoice.Status}.");
        }

        invoice.Status = InvoiceStatus.Void;
        invoice.VoidedAtUtc = _clock.GetUtcNow().UtcDateTime;

        try
        {
            await _invoices.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return GymResults.Conflict<InvoiceDto>($"Invoice {invoice.Number} was just updated. Refresh and try again.");
        }

        await _audit.RecordAsync(
            AuditCategory.Billing,
            "InvoiceVoided",
            AuditOutcome.Succeeded,
            resourceType: "Invoice",
            resourceId: invoice.Id.ToString(),
            reason: invoice.Number,
            cancellationToken: cancellationToken);

        return await GetInvoiceAsync(invoice.Id, cancellationToken);
    }

    public async Task<ServiceResult<SendReminderResultDto>> SendReminderAsync(
        int invoiceId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymBillingManage, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<SendReminderResultDto>(authorization);
        }

        var invoice = await _invoices.FirstOrDefaultAsync(x => x.Id == invoiceId, cancellationToken);

        if (invoice is null)
        {
            return GymResults.NotFound<SendReminderResultDto>("Invoice");
        }

        if (!invoice.IsUnpaid)
        {
            return GymResults.Conflict<SendReminderResultDto>($"Invoice {invoice.Number} is {invoice.Status}; no reminder needed.");
        }

        var member = await _members.FirstOrDefaultAsync(x => x.Id == invoice.MemberProfileId, cancellationToken);
        var gym = await _engine.GetGymAsync(invoice.TenantId, cancellationToken);

        if (member is null || gym is null)
        {
            return GymResults.NotFound<SendReminderResultDto>("Member");
        }

        _engine.QueueReminder(gym, invoice, member, $"manual:{Guid.NewGuid():N}");

        await _invoices.SaveChangesAsync(cancellationToken);

        await _audit.RecordAsync(
            AuditCategory.Billing,
            "PaymentReminderSent",
            AuditOutcome.Succeeded,
            resourceType: "Invoice",
            resourceId: invoice.Id.ToString(),
            reason: $"{invoice.Number} to {member.FullName}.",
            cancellationToken: cancellationToken);

        return ServiceResult<SendReminderResultDto>.Ok(new SendReminderResultDto
        {
            InvoiceId = invoice.Id,
            MemberName = member.FullName,
            EmailQueued = member.AnonymizedAtUtc is null
        });
    }

    public async Task<ServiceResult<PagedResult<PaymentDto>>> ListPaymentsAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymBillingRead, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<PagedResult<PaymentDto>>(authorization);
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var total = await _payments.QueryCountAsync(q => q, cancellationToken);

        var items = await _payments.QueryAsync(
            q => q
                .OrderByDescending(x => x.PaidAtUtc)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(ProjectPayment),
            cancellationToken);

        return ServiceResult<PagedResult<PaymentDto>>.Ok(new PagedResult<PaymentDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<ServiceResult<List<RevenuePointDto>>> GetRevenueByMonthAsync(
        int months,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymBillingRead, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<List<RevenuePointDto>>(authorization);
        }

        months = Math.Clamp(months, 1, 24);

        var gym = await _engine.GetGymAsync(_access.TenantId!.Value, cancellationToken);
        var timeZone = gym?.Settings.TimeZoneId;
        var today = GymCalendar.Today(_clock, timeZone);
        var firstMonth = GymCalendar.MonthStart(today).AddMonths(-(months - 1));
        var fromUtc = GymCalendar.StartOfDayUtc(firstMonth, timeZone);

        var payments = await _payments.QueryAsync(
            q => q
                .Where(x => x.PaidAtUtc >= fromUtc)
                .Select(x => new { x.PaidAtUtc, x.Amount }),
            cancellationToken);

        var byMonth = payments
            .GroupBy(x => GymCalendar.MonthStart(GymCalendar.ToLocalDate(x.PaidAtUtc, timeZone)))
            .ToDictionary(x => x.Key, x => x.Sum(p => p.Amount));

        var points = Enumerable.Range(0, months)
            .Select(i => firstMonth.AddMonths(i))
            .Select(month => new RevenuePointDto
            {
                Month = month,
                Collected = byMonth.GetValueOrDefault(month)
            })
            .ToList();

        return ServiceResult<List<RevenuePointDto>>.Ok(points);
    }

    public async Task<ServiceResult<List<InvoiceDto>>> ListMyInvoicesAsync(
        CancellationToken cancellationToken = default)
    {
        var member = await _access.GetCurrentMemberAsync(Permissions.GymSelfRead, cancellationToken: cancellationToken);

        if (!member.Success)
        {
            return GymResults.From<List<InvoiceDto>, MemberProfile>(member);
        }

        var memberId = member.Data!.Id;

        var items = await _invoices.QueryAsync(
            q => q
                .Where(x => x.MemberProfileId == memberId && x.Status != InvoiceStatus.Void)
                .OrderByDescending(x => x.DueDate)
                .Take(50)
                .Select(ProjectInvoice),
            cancellationToken);

        await FillOverdueDaysAsync(items, cancellationToken);

        return ServiceResult<List<InvoiceDto>>.Ok(items);
    }

    public async Task<ServiceResult<List<PaymentDto>>> ListMyPaymentsAsync(
        CancellationToken cancellationToken = default)
    {
        var member = await _access.GetCurrentMemberAsync(Permissions.GymSelfRead, cancellationToken: cancellationToken);

        if (!member.Success)
        {
            return GymResults.From<List<PaymentDto>, MemberProfile>(member);
        }

        var memberId = member.Data!.Id;

        var items = await _payments.QueryAsync(
            q => q
                .Where(x => x.MemberProfileId == memberId)
                .OrderByDescending(x => x.PaidAtUtc)
                .Take(50)
                .Select(ProjectPayment),
            cancellationToken);

        return ServiceResult<List<PaymentDto>>.Ok(items);
    }

    public async Task<ServiceResult<CheckoutSessionDto>> StartMyCheckoutAsync(
        int invoiceId,
        CancellationToken cancellationToken = default)
    {
        var member = await _access.GetCurrentMemberAsync(Permissions.GymSelfPayments, cancellationToken: cancellationToken);

        if (!member.Success)
        {
            return GymResults.From<CheckoutSessionDto, MemberProfile>(member);
        }

        var invoice = await _invoices.FirstOrDefaultTrackedAsync(
            x => x.Id == invoiceId && x.MemberProfileId == member.Data!.Id,
            cancellationToken);

        if (invoice is null)
        {
            return GymResults.NotFound<CheckoutSessionDto>("Invoice");
        }

        if (!invoice.IsUnpaid)
        {
            return GymResults.Conflict<CheckoutSessionDto>($"Invoice {invoice.Number} is already {invoice.Status}.");
        }

        var gym = await _engine.GetGymAsync(invoice.TenantId, cancellationToken);

        if (gym?.Settings is null || !_gateway.CanTakePayments(gym.Settings))
        {
            return GymResults.Conflict<CheckoutSessionDto>(
                $"{gym?.Name ?? "This gym"} does not take online payments yet. Pay at the front desk or as the gym describes.");
        }

        var checkout = await _gateway.CreateCheckoutAsync(
            new CheckoutRequest
            {
                TenantId = invoice.TenantId,
                InvoiceId = invoice.Id,
                InvoiceNumber = invoice.Number,
                Description = $"{gym?.Name}: {invoice.Description}",
                Amount = invoice.Amount,
                Currency = invoice.Currency,
                CustomerEmail = member.Data!.Email,
                ConnectedAccountId = gym?.Settings is { StripeChargesEnabled: true } settings
                    ? settings.StripeConnectedAccountId
                    : null
            },
            cancellationToken);

        invoice.CheckoutSessionId = checkout.SessionId;

        await _invoices.SaveChangesAsync(cancellationToken);

        return ServiceResult<CheckoutSessionDto>.Ok(new CheckoutSessionDto
        {
            Url = checkout.Url,
            Simulated = _gateway.IsSimulated
        });
    }

    private static readonly System.Linq.Expressions.Expression<Func<Invoice, InvoiceDto>> ProjectInvoice =
        x => new InvoiceDto
        {
            Id = x.Id,
            Number = x.Number,
            MemberId = x.MemberProfileId,
            MemberName = x.MemberProfile!.FullName,
            Description = x.Description,
            PeriodStart = x.PeriodStart,
            PeriodEnd = x.PeriodEnd,
            Amount = x.Amount,
            Currency = x.Currency,
            DueDate = x.DueDate,
            Status = x.Status,
            IssuedAtUtc = x.IssuedAtUtc,
            PaidAtUtc = x.PaidAtUtc
        };

    private static readonly System.Linq.Expressions.Expression<Func<Payment, PaymentDto>> ProjectPayment =
        x => new PaymentDto
        {
            Id = x.Id,
            InvoiceId = x.InvoiceId,
            InvoiceNumber = x.Invoice!.Number,
            MemberId = x.MemberProfileId,
            MemberName = x.Invoice.MemberProfile!.FullName,
            Amount = x.Amount,
            Currency = x.Currency,
            Method = x.Method,
            Provider = x.Provider,
            PaidAtUtc = x.PaidAtUtc
        };

    private async Task FillOverdueDaysAsync(List<InvoiceDto> invoices, CancellationToken cancellationToken)
    {
        if (invoices.Count == 0 || _access.TenantId is not { } tenantId)
            return;

        var gym = await _engine.GetGymAsync(tenantId, cancellationToken);
        var today = GymCalendar.Today(_clock, gym?.Settings.TimeZoneId);

        foreach (var invoice in invoices)
        {
            invoice.DaysOverdue = invoice.Status is InvoiceStatus.Open or InvoiceStatus.Overdue
                ? Math.Max(0, today.DayNumber - invoice.DueDate.DayNumber)
                : 0;
        }
    }
}
