using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Commons;
using IronForge.Application.Entities;
using IronForge.Application.Gym.Billing;
using IronForge.Application.Gym.Common;
using IronForge.Application.Gym.Notifications;
using IronForge.Application.Persistence;
using IronForge.Application.Tenants.Invitations;
using IronForge.Application.Tenants.Subscriptions;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Gym;
using IronForge.Shared.Models.Tenants;
using IronForge.Shared.Subscriptions;

namespace IronForge.Application.Gym.Members;

public interface IMemberService
{
    Task<ServiceResult<PagedResult<MemberSummaryDto>>> ListAsync(
        MemberQuery query,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<MemberDetailDto>> GetAsync(
        int memberId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds an active member, issues the first invoice and optionally invites them to the app.</summary>
    Task<ServiceResult<MemberSummaryDto>> CreateAsync(
        CreateMemberRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<MemberSummaryDto>> UpdateAsync(
        int memberId,
        UpdateMemberRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Approves a join request: activates the member and issues the first invoice.</summary>
    Task<ServiceResult<MemberSummaryDto>> ApproveAsync(
        int memberId,
        ApproveMemberRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Declines a join request; the profile is cancelled.</summary>
    Task<ServiceResult<MemberSummaryDto>> RejectAsync(
        int memberId,
        CancellationToken cancellationToken = default);

    /// <summary>Pauses billing until a date; the paid period is extended by the freeze.</summary>
    Task<ServiceResult<MemberSummaryDto>> FreezeAsync(
        int memberId,
        FreezeMemberRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<MemberSummaryDto>> UnfreezeAsync(
        int memberId,
        CancellationToken cancellationToken = default);

    /// <summary>Ends the membership; unpaid invoices for future periods are voided.</summary>
    Task<ServiceResult<MemberSummaryDto>> CancelAsync(
        int memberId,
        CancellationToken cancellationToken = default);

    /// <summary>Restarts a cancelled or expired membership from today.</summary>
    Task<ServiceResult<MemberSummaryDto>> ReactivateAsync(
        int memberId,
        CancellationToken cancellationToken = default);

    /// <summary>The new plan applies from the next invoice.</summary>
    Task<ServiceResult<MemberSummaryDto>> ChangePlanAsync(
        int memberId,
        ChangeMemberPlanRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Erases the member's personal and fitness data on request. Invoices and
    /// payments are kept for accounting, under an anonymous name.
    /// </summary>
    Task<ServiceResult<bool>> AnonymizeAsync(
        int memberId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Emails a member without an app account a link to choose their username
    /// and password, and returns the link so staff can also share it directly.
    /// A new link replaces the previous one.
    /// </summary>
    Task<ServiceResult<TenantInvitationLinkDto>> SendAppInvitationAsync(
        int memberId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<MyMembershipDto>> GetMyMembershipAsync(
        CancellationToken cancellationToken = default);

    Task<ServiceResult<MyMembershipDto>> UpdateMyProfileAsync(
        UpdateMyProfileRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class MemberService : IMemberService
{
    private readonly IGymAccessService _access;
    private readonly IRepository<MemberProfile> _members;
    private readonly IRepository<MembershipPlan> _plans;
    private readonly IRepository<Invoice> _invoices;
    private readonly IRepository<CheckIn> _checkIns;
    private readonly IRepository<WorkoutRoutine> _routines;
    private readonly IRepository<WorkoutLog> _workouts;
    private readonly IRepository<BodyMetric> _metrics;
    private readonly IRepository<FitnessGoal> _goals;
    private readonly IRepository<MemberPhoto> _photos;
    private readonly IRepository<TenantMembership> _memberships;
    private readonly IRepository<User> _users;
    private readonly IBillingEngine _billing;
    private readonly ITenantInvitationService _invitations;
    private readonly ITenantEntitlementService _entitlements;
    private readonly INotificationService _notifications;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;
    private readonly IPaymentGateway _payments;

    public MemberService(
        IGymAccessService access,
        IRepository<MemberProfile> members,
        IRepository<MembershipPlan> plans,
        IRepository<Invoice> invoices,
        IRepository<CheckIn> checkIns,
        IRepository<WorkoutRoutine> routines,
        IRepository<WorkoutLog> workouts,
        IRepository<BodyMetric> metrics,
        IRepository<FitnessGoal> goals,
        IRepository<MemberPhoto> photos,
        IRepository<TenantMembership> memberships,
        IRepository<User> users,
        IBillingEngine billing,
        ITenantInvitationService invitations,
        ITenantEntitlementService entitlements,
        INotificationService notifications,
        IUnitOfWork unitOfWork,
        IAuditService audit,
        TimeProvider clock,
        IPaymentGateway payments)
    {
        _payments = payments;
        _access = access;
        _members = members;
        _plans = plans;
        _invoices = invoices;
        _checkIns = checkIns;
        _routines = routines;
        _workouts = workouts;
        _metrics = metrics;
        _goals = goals;
        _photos = photos;
        _memberships = memberships;
        _users = users;
        _billing = billing;
        _invitations = invitations;
        _entitlements = entitlements;
        _notifications = notifications;
        _unitOfWork = unitOfWork;
        _audit = audit;
        _clock = clock;
    }

    public async Task<ServiceResult<PagedResult<MemberSummaryDto>>> ListAsync(
        MemberQuery query,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymMembersRead, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<PagedResult<MemberSummaryDto>>(authorization);
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var search = query.Search?.Trim().ToLower();

        IQueryable<MemberProfile> Filter(IQueryable<MemberProfile> q)
        {
            q = q.Where(x => x.AnonymizedAtUtc == null);

            if (query.Status.HasValue)
                q = q.Where(x => x.Status == query.Status.Value);
            if (query.MembershipPlanId.HasValue)
                q = q.Where(x => x.MembershipPlanId == query.MembershipPlanId.Value);
            if (query.OverdueOnly)
                q = q.Where(x => x.Invoices.Any(i => i.Status == InvoiceStatus.Overdue));
            if (!string.IsNullOrEmpty(search))
                q = q.Where(x =>
                    x.FullName.ToLower().Contains(search) ||
                    x.Email.Contains(search) ||
                    (x.Phone != null && x.Phone.Contains(search)));
            return q;
        }

        var total = await _members.QueryCountAsync(Filter, cancellationToken);

        var items = await _members.QueryAsync(
            q => Filter(q)
                .OrderBy(x => x.FullName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(ProjectSummary),
            cancellationToken);

        return ServiceResult<PagedResult<MemberSummaryDto>>.Ok(new PagedResult<MemberSummaryDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<ServiceResult<MemberDetailDto>> GetAsync(
        int memberId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymMembersRead, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<MemberDetailDto>(authorization);
        }

        var member = await _members.FirstOrDefaultAsync(
            x => x.Id == memberId && x.AnonymizedAtUtc == null,
            cancellationToken);

        if (member is null)
        {
            return GymResults.NotFound<MemberDetailDto>("Member");
        }

        var summary = (await _members.QueryAsync(
            q => q.Where(x => x.Id == memberId).Select(ProjectSummary),
            cancellationToken)).Single();

        var since = _clock.GetUtcNow().UtcDateTime.AddDays(-30);

        var detail = new MemberDetailDto
        {
            Summary = summary,
            DateOfBirth = member.DateOfBirth,
            Gender = member.Gender,
            HeightCm = member.HeightCm,
            EmergencyContact = member.EmergencyContact,
            Notes = member.Notes,
            FrozenUntil = member.FrozenUntil,
            CheckInsLast30Days = await _checkIns.CountAsync(
                x => x.MemberProfileId == memberId && x.CheckedInAtUtc >= since,
                cancellationToken),
            RecentInvoices = await _invoices.QueryAsync(
                q => q
                    .Where(x => x.MemberProfileId == memberId)
                    .OrderByDescending(x => x.DueDate)
                    .Take(12)
                    .Select(x => new InvoiceDto
                    {
                        Id = x.Id,
                        Number = x.Number,
                        MemberId = x.MemberProfileId,
                        MemberName = member.FullName,
                        Description = x.Description,
                        PeriodStart = x.PeriodStart,
                        PeriodEnd = x.PeriodEnd,
                        Amount = x.Amount,
                        Currency = x.Currency,
                        DueDate = x.DueDate,
                        Status = x.Status,
                        IssuedAtUtc = x.IssuedAtUtc,
                        PaidAtUtc = x.PaidAtUtc
                    }),
                cancellationToken),
            RecentCheckIns = await _checkIns.QueryAsync(
                q => q
                    .Where(x => x.MemberProfileId == memberId)
                    .OrderByDescending(x => x.CheckedInAtUtc)
                    .Take(10)
                    .Select(x => new CheckInDto
                    {
                        Id = x.Id,
                        MemberId = x.MemberProfileId,
                        MemberName = member.FullName,
                        CheckedInAtUtc = x.CheckedInAtUtc,
                        SelfCheckIn = x.Method == CheckInMethod.Kiosk || x.Method == CheckInMethod.GymQr || x.Method == CheckInMethod.Terminal,
                    Warning = x.Warning,
                        Method = x.Method,
                        DeviceName = x.CheckInDevice != null ? x.CheckInDevice.Name : null
                    }),
                cancellationToken),
            Routines = await _routines.QueryAsync(
                q => q
                    .Where(x => x.MemberProfileId == memberId)
                    .OrderByDescending(x => x.IsActive)
                    .ThenByDescending(x => x.CreatedAtUtc)
                    .Select(x => new RoutineSummaryDto
                    {
                        Id = x.Id,
                        Name = x.Name,
                        Goal = x.Goal,
                        Source = x.Source,
                        IsActive = x.IsActive,
                        DayCount = x.Days.Count,
                        CreatedAtUtc = x.CreatedAtUtc
                    }),
                cancellationToken),
            Goals = await _goals.QueryAsync(
                q => q
                    .Where(x => x.MemberProfileId == memberId)
                    .OrderBy(x => x.Status)
                    .ThenByDescending(x => x.CreatedAtUtc)
                    .Select(x => new FitnessGoalDto
                    {
                        Id = x.Id,
                        Type = x.Type,
                        Description = x.Description,
                        TargetValue = x.TargetValue,
                        TargetUnit = x.TargetUnit,
                        TargetDate = x.TargetDate,
                        Status = x.Status,
                        CreatedAtUtc = x.CreatedAtUtc
                    }),
                cancellationToken)
        };

        if (member.PhotoUpdatedAtUtc is not null)
        {
            var photo = (await _photos.QueryAsync(
                q => q.Where(x => x.MemberProfileId == memberId),
                cancellationToken)).SingleOrDefault();

            detail.Photo = photo is null
                ? null
                : new MemberPhotoDto
                {
                    DataUrl = $"data:{photo.ContentType};base64,{Convert.ToBase64String(photo.Data)}",
                    UploadedByStaff = photo.UploadedByStaff,
                    UploadedAtUtc = photo.UploadedAtUtc
                };
        }

        return ServiceResult<MemberDetailDto>.Ok(detail);
    }

    public async Task<ServiceResult<MemberSummaryDto>> CreateAsync(
        CreateMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymMembersManage, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<MemberSummaryDto>(authorization);
        }

        var tenantId = _access.TenantId!.Value;
        var email = GymCalendar.NormalizeEmail(request.Email);

        if (await _members.AnyAsync(x => x.Email == email, cancellationToken))
        {
            return GymResults.Conflict<MemberSummaryDto>($"A member with the email {email} already exists.");
        }

        var plan = await _plans.FirstOrDefaultAsync(
            x => x.Id == request.MembershipPlanId && x.IsActive,
            cancellationToken);

        if (plan is null)
        {
            return GymResults.Invalid<MemberSummaryDto>("Choose an active membership plan.");
        }

        var gym = await _billing.GetGymAsync(tenantId, cancellationToken);

        if (gym is null)
        {
            return GymResults.NotFound<MemberSummaryDto>("Gym settings");
        }

        var today = GymCalendar.Today(_clock, gym.Settings.TimeZoneId);
        var start = request.StartDate ?? today;

        if (start < today.AddDays(-31) || start > today.AddDays(90))
        {
            return GymResults.Invalid<MemberSummaryDto>("The start date must be within the last month or the next 90 days.");
        }

        // An existing account with this email is linked straight away.
        var existingUser = await _users.FirstOrDefaultAsync(
            x => x.Email.ToLower() == email,
            cancellationToken);

        var now = _clock.GetUtcNow().UtcDateTime;

        var member = await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var created = new MemberProfile
            {
                TenantId = tenantId,
                FullName = request.FullName.Trim(),
                Email = email,
                Phone = Clean(request.Phone),
                MembershipPlanId = plan.Id,
                Status = MemberStatus.Active,
                JoinedAtUtc = now,
                ApprovedAtUtc = now,
                NextBillingDate = start
            };

            _members.Add(created);
            await _members.SaveChangesAsync(cancellationToken);

            await _billing.IssueNextInvoiceAsync(gym, created, plan, cancellationToken);
            await _members.SaveChangesAsync(cancellationToken);

            return created;
        }, cancellationToken);

        if (existingUser is not null)
        {
            await LinkUserAsync(member, existingUser.Id, cancellationToken);
        }
        else if (request.SendAppInvitation)
        {
            // Accepting the invitation creates the account and links this profile by email.
            await _invitations.IssueAsync(tenantId, email, TenantRoles.Member, cancellationToken);
        }

        await _audit.RecordAsync(
            AuditCategory.Membership,
            "MemberCreated",
            AuditOutcome.Succeeded,
            resourceType: "MemberProfile",
            resourceId: member.Id.ToString(),
            reason: $"{member.FullName} on {plan.Name}.",
            cancellationToken: cancellationToken);

        return await SummaryAsync(member.Id, cancellationToken);
    }

    public async Task<ServiceResult<MemberSummaryDto>> UpdateAsync(
        int memberId,
        UpdateMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        var (member, failure) = await LoadForChangeAsync(memberId, cancellationToken);

        if (member is null)
        {
            return failure!;
        }

        member.FullName = request.FullName.Trim();
        member.Phone = Clean(request.Phone);
        member.DateOfBirth = request.DateOfBirth;
        member.Gender = Clean(request.Gender);
        member.HeightCm = request.HeightCm;
        member.EmergencyContact = Clean(request.EmergencyContact);
        member.Notes = Clean(request.Notes);

        await _members.SaveChangesAsync(cancellationToken);

        await RecordAsync("MemberUpdated", member, null, cancellationToken);

        return await SummaryAsync(member.Id, cancellationToken);
    }

    public async Task<ServiceResult<MemberSummaryDto>> ApproveAsync(
        int memberId,
        ApproveMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        var (member, failure) = await LoadForChangeAsync(memberId, cancellationToken);

        if (member is null)
        {
            return failure!;
        }

        if (member.Status != MemberStatus.Pending)
        {
            return GymResults.Conflict<MemberSummaryDto>($"{member.FullName} is not waiting for approval.");
        }

        var planId = request.MembershipPlanId ?? member.MembershipPlanId;

        var plan = planId is null
            ? null
            : await _plans.FirstOrDefaultAsync(x => x.Id == planId && x.IsActive, cancellationToken);

        if (plan is null)
        {
            return GymResults.Invalid<MemberSummaryDto>("Choose an active membership plan for this member.");
        }

        var gym = await _billing.GetGymAsync(member.TenantId, cancellationToken);

        if (gym is null)
        {
            return GymResults.NotFound<MemberSummaryDto>("Gym settings");
        }

        member.MembershipPlanId = plan.Id;
        member.Status = MemberStatus.Active;
        member.ApprovedAtUtc = _clock.GetUtcNow().UtcDateTime;

        // Approval opens the gym in the member's app.
        await SetAppAccessAsync(member, true, cancellationToken);
        member.NextBillingDate ??= GymCalendar.Today(_clock, gym.Settings.TimeZoneId);

        var invoice = await _billing.IssueNextInvoiceAsync(gym, member, plan, cancellationToken);

        _notifications.Queue(
            member.TenantId,
            [BillingEngine.Recipient(member)],
            new NotificationMessage
            {
                Type = NotificationType.Membership,
                Title = $"Welcome to {gym.Name}!",
                Body =
                    $"Your membership on the {plan.Name} plan is approved.\n\n" +
                    $"Your first invoice ({BillingEngine.FormatMoney(invoice.Amount, invoice.Currency)}) is due on " +
                    $"{BillingEngine.Format(invoice.DueDate)}. You can pay it in the app.",
                Link = "/home",
                SendEmail = true,
                GymName = gym.Name
            });

        await _members.SaveChangesAsync(cancellationToken);

        await RecordAsync("MemberApproved", member, plan.Name, cancellationToken);

        return await SummaryAsync(member.Id, cancellationToken);
    }

    public async Task<ServiceResult<MemberSummaryDto>> RejectAsync(
        int memberId,
        CancellationToken cancellationToken = default)
    {
        var (member, failure) = await LoadForChangeAsync(memberId, cancellationToken);

        if (member is null)
        {
            return failure!;
        }

        if (member.Status != MemberStatus.Pending)
        {
            return GymResults.Conflict<MemberSummaryDto>($"{member.FullName} is not waiting for approval.");
        }

        member.Status = MemberStatus.Cancelled;
        member.CancelledAtUtc = _clock.GetUtcNow().UtcDateTime;
        member.NextBillingDate = null;

        // A declined request gives no access to the gym.
        await SetAppAccessAsync(member, false, cancellationToken);

        _notifications.Queue(
            member.TenantId,
            [BillingEngine.Recipient(member)],
            new NotificationMessage
            {
                Type = NotificationType.Membership,
                Title = "Your join request was declined",
                Body = "The gym declined your request to join. Contact the front desk if you think this is a mistake.",
                SendEmail = true,
                GymName = (await _billing.GetGymAsync(member.TenantId, cancellationToken))?.Name
            });

        await _members.SaveChangesAsync(cancellationToken);

        await RecordAsync("MemberRejected", member, null, cancellationToken);

        return await SummaryAsync(member.Id, cancellationToken);
    }

    public async Task<ServiceResult<MemberSummaryDto>> FreezeAsync(
        int memberId,
        FreezeMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        var (member, failure) = await LoadForChangeAsync(memberId, cancellationToken);

        if (member is null)
        {
            return failure!;
        }

        if (member.Status != MemberStatus.Active)
        {
            return GymResults.Conflict<MemberSummaryDto>($"Only active memberships can be frozen; {member.FullName} is {member.Status}.");
        }

        var gym = await _billing.GetGymAsync(member.TenantId, cancellationToken);
        var today = GymCalendar.Today(_clock, gym?.Settings.TimeZoneId);

        if (request.Until < today || request.Until > today.AddDays(180))
        {
            return GymResults.Invalid<MemberSummaryDto>("A freeze must end within the next 180 days.");
        }

        // The member keeps the days they paid for: billing and the paid
        // period move forward by the length of the freeze.
        var days = request.Until.DayNumber - today.DayNumber + 1;

        member.Status = MemberStatus.Frozen;
        member.FrozenUntil = request.Until;

        if (member.NextBillingDate is { } next)
            member.NextBillingDate = next.AddDays(days);

        if (member.PaidThrough is { } paid && paid >= today)
            member.PaidThrough = paid.AddDays(days);

        _notifications.Queue(
            member.TenantId,
            [BillingEngine.Recipient(member)],
            new NotificationMessage
            {
                Type = NotificationType.Membership,
                Title = "Your membership is frozen",
                Body =
                    $"Your membership is paused until {BillingEngine.Format(request.Until)}. " +
                    "You will not be billed during the freeze.",
                Link = "/membership",
                SendEmail = true,
                GymName = gym?.Name
            });

        await _members.SaveChangesAsync(cancellationToken);

        await RecordAsync("MemberFrozen", member, $"Until {request.Until:yyyy-MM-dd}. {request.Reason}".Trim(), cancellationToken);

        return await SummaryAsync(member.Id, cancellationToken);
    }

    public async Task<ServiceResult<MemberSummaryDto>> UnfreezeAsync(
        int memberId,
        CancellationToken cancellationToken = default)
    {
        var (member, failure) = await LoadForChangeAsync(memberId, cancellationToken);

        if (member is null)
        {
            return failure!;
        }

        if (member.Status != MemberStatus.Frozen)
        {
            return GymResults.Conflict<MemberSummaryDto>($"{member.FullName} is not frozen.");
        }

        member.Status = MemberStatus.Active;
        member.FrozenUntil = null;

        await _members.SaveChangesAsync(cancellationToken);

        await RecordAsync("MemberUnfrozen", member, null, cancellationToken);

        return await SummaryAsync(member.Id, cancellationToken);
    }

    public async Task<ServiceResult<MemberSummaryDto>> CancelAsync(
        int memberId,
        CancellationToken cancellationToken = default)
    {
        var (member, failure) = await LoadForChangeAsync(memberId, cancellationToken);

        if (member is null)
        {
            return failure!;
        }

        if (member.Status == MemberStatus.Cancelled)
        {
            return GymResults.Conflict<MemberSummaryDto>($"{member.FullName} is already cancelled.");
        }

        var gym = await _billing.GetGymAsync(member.TenantId, cancellationToken);
        var today = GymCalendar.Today(_clock, gym?.Settings.TimeZoneId);
        var now = _clock.GetUtcNow().UtcDateTime;

        var future = await _invoices.ListTrackedAsync(
            x => x.MemberProfileId == member.Id &&
                 x.Status == InvoiceStatus.Open &&
                 x.PeriodStart > today,
            cancellationToken);

        foreach (var invoice in future)
        {
            invoice.Status = InvoiceStatus.Void;
            invoice.VoidedAtUtc = now;
        }

        member.Status = MemberStatus.Cancelled;
        member.CancelledAtUtc = now;
        member.NextBillingDate = null;
        member.FrozenUntil = null;

        _notifications.Queue(
            member.TenantId,
            [BillingEngine.Recipient(member)],
            new NotificationMessage
            {
                Type = NotificationType.Membership,
                Title = "Your membership was cancelled",
                Body = member.PaidThrough is { } paid && paid >= today
                    ? $"Your membership is cancelled. You can keep training until {BillingEngine.Format(paid)}."
                    : "Your membership is cancelled. We hope to see you again!",
                Link = "/membership",
                SendEmail = true,
                GymName = gym?.Name
            });

        try
        {
            await _members.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return GymResults.Conflict<MemberSummaryDto>("An invoice was just paid. Refresh and try again.");
        }

        await RecordAsync("MemberCancelled", member, $"{future.Count} future invoice(s) voided.", cancellationToken);

        return await SummaryAsync(member.Id, cancellationToken);
    }

    public async Task<ServiceResult<MemberSummaryDto>> ReactivateAsync(
        int memberId,
        CancellationToken cancellationToken = default)
    {
        var (member, failure) = await LoadForChangeAsync(memberId, cancellationToken);

        if (member is null)
        {
            return failure!;
        }

        if (member.Status is not (MemberStatus.Cancelled or MemberStatus.Expired))
        {
            return GymResults.Conflict<MemberSummaryDto>($"{member.FullName} is {member.Status}; only cancelled or expired memberships can be reactivated.");
        }

        var plan = member.MembershipPlanId is { } planId
            ? await _plans.FirstOrDefaultAsync(x => x.Id == planId && x.IsActive, cancellationToken)
            : null;

        if (plan is null)
        {
            return GymResults.Invalid<MemberSummaryDto>("The member's plan is archived. Change the plan first.");
        }

        var gym = await _billing.GetGymAsync(member.TenantId, cancellationToken);

        if (gym is null)
        {
            return GymResults.NotFound<MemberSummaryDto>("Gym settings");
        }

        var today = GymCalendar.Today(_clock, gym.Settings.TimeZoneId);
        var wasCancelled = member.Status == MemberStatus.Cancelled;

        member.Status = MemberStatus.Active;
        member.CancelledAtUtc = null;
        member.ApprovedAtUtc ??= _clock.GetUtcNow().UtcDateTime;

        await SetAppAccessAsync(member, true, cancellationToken);

        // A cancelled member starts a new period today; an expired member
        // continues where billing stopped (their overdue invoice stays open).
        if (wasCancelled || member.NextBillingDate is null)
        {
            member.NextBillingDate = member.PaidThrough is { } paid && paid >= today
                ? paid.AddDays(1)
                : today;

            if (member.NextBillingDate <= today)
            {
                await _billing.IssueNextInvoiceAsync(gym, member, plan, cancellationToken);
            }
        }

        await _members.SaveChangesAsync(cancellationToken);

        await RecordAsync("MemberReactivated", member, plan.Name, cancellationToken);

        return await SummaryAsync(member.Id, cancellationToken);
    }

    public async Task<ServiceResult<MemberSummaryDto>> ChangePlanAsync(
        int memberId,
        ChangeMemberPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        var (member, failure) = await LoadForChangeAsync(memberId, cancellationToken);

        if (member is null)
        {
            return failure!;
        }

        var plan = await _plans.FirstOrDefaultAsync(
            x => x.Id == request.MembershipPlanId && x.IsActive,
            cancellationToken);

        if (plan is null)
        {
            return GymResults.Invalid<MemberSummaryDto>("Choose an active membership plan.");
        }

        member.MembershipPlanId = plan.Id;

        await _members.SaveChangesAsync(cancellationToken);

        await RecordAsync("MemberPlanChanged", member, plan.Name, cancellationToken);

        return await SummaryAsync(member.Id, cancellationToken);
    }

    public async Task<ServiceResult<bool>> AnonymizeAsync(
        int memberId,
        CancellationToken cancellationToken = default)
    {
        var (member, failure) = await LoadForChangeAsync(memberId, cancellationToken);

        if (member is null)
        {
            return GymResults.From<bool, MemberSummaryDto>(failure!);
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var userId = member.UserId;

        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            foreach (var item in await _metrics.ListTrackedAsync(x => x.MemberProfileId == memberId, cancellationToken))
                _metrics.Remove(item);
            foreach (var item in await _goals.ListTrackedAsync(x => x.MemberProfileId == memberId, cancellationToken))
                _goals.Remove(item);
            foreach (var item in await _workouts.ListTrackedAsync(x => x.MemberProfileId == memberId, cancellationToken))
                _workouts.Remove(item);
            foreach (var item in await _routines.ListTrackedAsync(x => x.MemberProfileId == memberId, cancellationToken))
                _routines.Remove(item);
            foreach (var item in await _photos.ListTrackedAsync(x => x.MemberProfileId == memberId, cancellationToken))
                _photos.Remove(item);

            if (userId is { } id)
            {
                var membership = await _memberships.FirstOrDefaultTrackedAsync(
                    x => x.TenantId == member.TenantId && x.UserId == id,
                    cancellationToken);

                if (membership is not null)
                {
                    membership.IsActive = false;
                }
            }

            member.FullName = "Deleted member";
            member.Email = $"deleted-{member.Id}@anonymized.invalid";
            member.Phone = null;
            member.DateOfBirth = null;
            member.Gender = null;
            member.HeightCm = null;
            member.EmergencyContact = null;
            member.Notes = null;
            member.UserId = null;
            member.Status = MemberStatus.Cancelled;
            member.CancelledAtUtc ??= now;
            member.NextBillingDate = null;
            member.AnonymizedAtUtc = now;
            member.PhotoUpdatedAtUtc = null;
            member.PassKeyVersion++;

            await _members.SaveChangesAsync(cancellationToken);

            return true;
        }, cancellationToken);

        await _audit.RecordAsync(
            AuditCategory.Membership,
            "MemberAnonymized",
            AuditOutcome.Succeeded,
            resourceType: "MemberProfile",
            resourceId: memberId.ToString(),
            cancellationToken: cancellationToken);

        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<MyMembershipDto>> GetMyMembershipAsync(
        CancellationToken cancellationToken = default)
    {
        var current = await _access.GetCurrentMemberAsync(Permissions.GymSelfRead, cancellationToken: cancellationToken);

        if (!current.Success)
        {
            return GymResults.From<MyMembershipDto, MemberProfile>(current);
        }

        var member = current.Data!;
        var gym = await _billing.GetGymAsync(member.TenantId, cancellationToken);
        var timeZone = gym?.Settings.TimeZoneId;
        var currency = gym?.Settings.Currency ?? "USD";
        var today = GymCalendar.Today(_clock, timeZone);

        var plan = member.MembershipPlanId is { } planId
            ? await _plans.FirstOrDefaultAsync(x => x.Id == planId, cancellationToken)
            : null;

        var unpaid = await _invoices.QueryAsync(
            q => q
                .Where(x => x.MemberProfileId == member.Id &&
                            (x.Status == InvoiceStatus.Open || x.Status == InvoiceStatus.Overdue))
                .OrderBy(x => x.DueDate),
            cancellationToken);

        var workoutDays = await _workouts.QueryAsync(
            q => q
                .Where(x => x.MemberProfileId == member.Id && x.PerformedOn >= today.AddDays(-400))
                .Select(x => x.PerformedOn),
            cancellationToken);

        var weekStart = GymCalendar.WeekStart(today);
        var since = _clock.GetUtcNow().UtcDateTime.AddDays(-30);

        var next = unpaid.FirstOrDefault();

        var dto = new MyMembershipDto
        {
            MemberId = member.Id,
            GymName = gym?.Name ?? "",
            FullName = member.FullName,
            Email = member.Email,
            Phone = member.Phone,
            DateOfBirth = member.DateOfBirth,
            Gender = member.Gender,
            HeightCm = member.HeightCm,
            EmergencyContact = member.EmergencyContact,
            Status = member.Status,
            Plan = plan is null ? null : MembershipPlanService.Map(plan, currency, 0),
            PaidThrough = member.PaidThrough,
            NextBillingDate = member.NextBillingDate,
            FrozenUntil = member.FrozenUntil,
            OutstandingAmount = unpaid.Sum(x => x.Amount),
            Currency = currency,
            WorkoutsThisWeek = workoutDays.Count(x => x >= weekStart),
            WeeklyStreak = GymCalendar.WeeklyStreak(workoutDays, today),
            CheckInsLast30Days = await _checkIns.CountAsync(
                x => x.MemberProfileId == member.Id && x.CheckedInAtUtc >= since,
                cancellationToken),
            CoachingEnabled = (await _entitlements.GetEnabledModulesAsync(cancellationToken))
                .Contains(PlatformModules.GymCoaching),
            HasPhoto = member.PhotoUpdatedAtUtc != null,
            MemberPassCheckInEnabled = gym?.Settings.MemberPassCheckInEnabled ?? false,
            GymQrCheckInEnabled = gym?.Settings.GymQrCheckInEnabled ?? false,
            RequirePhoneUnlock = gym?.Settings.RequirePhoneUnlock ?? true,
            OnlinePaymentAvailable = gym?.Settings is { } settings && _payments.CanTakePayments(settings),
            PaymentInstructions = gym?.Settings.PaymentInstructions,
            NextDueInvoice = next is null
                ? null
                : new InvoiceDto
                {
                    Id = next.Id,
                    Number = next.Number,
                    MemberId = member.Id,
                    MemberName = member.FullName,
                    Description = next.Description,
                    PeriodStart = next.PeriodStart,
                    PeriodEnd = next.PeriodEnd,
                    Amount = next.Amount,
                    Currency = next.Currency,
                    DueDate = next.DueDate,
                    Status = next.Status,
                    IssuedAtUtc = next.IssuedAtUtc,
                    DaysOverdue = Math.Max(0, today.DayNumber - next.DueDate.DayNumber)
                }
        };

        return ServiceResult<MyMembershipDto>.Ok(dto);
    }

    public async Task<ServiceResult<MyMembershipDto>> UpdateMyProfileAsync(
        UpdateMyProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var current = await _access.GetCurrentMemberAsync(
            Permissions.GymSelfRead,
            tracked: true,
            cancellationToken: cancellationToken);

        if (!current.Success)
        {
            return GymResults.From<MyMembershipDto, MemberProfile>(current);
        }

        var member = current.Data!;

        member.FullName = request.FullName.Trim();
        member.Phone = Clean(request.Phone);
        member.DateOfBirth = request.DateOfBirth;
        member.Gender = Clean(request.Gender);
        member.HeightCm = request.HeightCm;
        member.EmergencyContact = Clean(request.EmergencyContact);

        await _members.SaveChangesAsync(cancellationToken);

        return await GetMyMembershipAsync(cancellationToken);
    }

    /// <summary>Gives the member's app account a Member role in the gym and links the profile.</summary>
    private async Task LinkUserAsync(MemberProfile member, int userId, CancellationToken cancellationToken)
    {
        var tracked = await _members.FirstOrDefaultTrackedAsync(x => x.Id == member.Id, cancellationToken);

        if (tracked is null || await _members.AnyAsync(x => x.UserId == userId, cancellationToken))
        {
            return;
        }

        tracked.UserId = userId;

        var membership = await _memberships.FirstOrDefaultTrackedAsync(
            x => x.TenantId == member.TenantId && x.UserId == userId,
            cancellationToken);

        if (membership is null)
        {
            _memberships.Add(new TenantMembership
            {
                TenantId = member.TenantId,
                UserId = userId,
                Role = TenantRoles.Member,
                IsActive = true,
                CreatedAtUtc = _clock.GetUtcNow().UtcDateTime
            });
        }
        else if (!membership.IsActive)
        {
            membership.IsActive = true;
        }

        await _members.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Turns the member's app access to this gym on or off (their TenantMembership).</summary>
    private async Task SetAppAccessAsync(MemberProfile member, bool active, CancellationToken cancellationToken)
    {
        if (member.UserId is not { } userId)
            return;

        var membership = await _memberships.FirstOrDefaultTrackedAsync(
            x => x.TenantId == member.TenantId && x.UserId == userId && x.Role == TenantRoles.Member,
            cancellationToken);

        if (membership is not null)
            membership.IsActive = active;
    }

    private async Task<(MemberProfile? Member, ServiceResult<MemberSummaryDto>? Failure)> LoadForChangeAsync(
        int memberId,
        CancellationToken cancellationToken)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymMembersManage, cancellationToken);

        if (!authorization.Allowed)
        {
            return (null, GymResults.Forbidden<MemberSummaryDto>(authorization));
        }

        var member = await _members.FirstOrDefaultTrackedAsync(
            x => x.Id == memberId && x.AnonymizedAtUtc == null,
            cancellationToken);

        return member is null
            ? (null, GymResults.NotFound<MemberSummaryDto>("Member"))
            : (member, null);
    }

    public async Task<ServiceResult<TenantInvitationLinkDto>> SendAppInvitationAsync(
        int memberId,
        CancellationToken cancellationToken = default)
    {
        var (member, failure) = await LoadForChangeAsync(memberId, cancellationToken);

        if (member is null)
        {
            return GymResults.From<TenantInvitationLinkDto, MemberSummaryDto>(failure!);
        }

        if (member.UserId is not null)
        {
            return GymResults.Conflict<TenantInvitationLinkDto>(
                "This member already has an app account. They can sign in, or reset a forgotten password from the sign-in page.");
        }

        if (!member.IsApproved)
        {
            return GymResults.Invalid<TenantInvitationLinkDto>(
                "Approve the member first. Their app access starts with the approval.");
        }

        var invitation = await _invitations.IssueAsync(
            member.TenantId,
            member.Email,
            TenantRoles.Member,
            cancellationToken);

        if (invitation.Success)
        {
            await RecordAsync("MemberAppInvitationSent", member, $"invitation emailed to {member.Email}", cancellationToken);
        }

        return invitation;
    }

    private async Task<ServiceResult<MemberSummaryDto>> SummaryAsync(int memberId, CancellationToken cancellationToken)
    {
        var summary = (await _members.QueryAsync(
            q => q.Where(x => x.Id == memberId).Select(ProjectSummary),
            cancellationToken)).SingleOrDefault();

        return summary is null
            ? GymResults.NotFound<MemberSummaryDto>("Member")
            : ServiceResult<MemberSummaryDto>.Ok(summary);
    }

    private Task RecordAsync(string action, MemberProfile member, string? reason, CancellationToken cancellationToken) =>
        _audit.RecordAsync(
            AuditCategory.Membership,
            action,
            AuditOutcome.Succeeded,
            resourceType: "MemberProfile",
            resourceId: member.Id.ToString(),
            reason: reason is null ? member.FullName : $"{member.FullName}: {reason}",
            cancellationToken: cancellationToken);

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static readonly System.Linq.Expressions.Expression<Func<MemberProfile, MemberSummaryDto>> ProjectSummary =
        x => new MemberSummaryDto
        {
            Id = x.Id,
            UserId = x.UserId,
            FullName = x.FullName,
            Email = x.Email,
            Phone = x.Phone,
            Status = x.Status,
            MembershipPlanId = x.MembershipPlanId,
            PlanName = x.MembershipPlan != null ? x.MembershipPlan.Name : null,
            PaidThrough = x.PaidThrough,
            NextBillingDate = x.NextBillingDate,
            OutstandingAmount = x.Invoices
                .Where(i => i.Status == InvoiceStatus.Open || i.Status == InvoiceStatus.Overdue)
                .Sum(i => i.Amount),
            JoinedAtUtc = x.JoinedAtUtc,
            LastCheckInUtc = x.CheckIns
                .OrderByDescending(c => c.CheckedInAtUtc)
                .Select(c => (DateTime?)c.CheckedInAtUtc)
                .FirstOrDefault(),
            HasAppAccount = x.UserId != null,
            HasPhoto = x.PhotoUpdatedAtUtc != null
        };
}
