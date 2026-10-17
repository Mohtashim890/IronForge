using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Commons;
using IronForge.Application.Entities;
using IronForge.Application.Gym.Billing;
using IronForge.Application.Gym.Common;
using IronForge.Application.Persistence;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Gym;
using IronForge.Shared.Subscriptions;

namespace IronForge.Application.Gym.Notifications;

public interface IAnnouncementService
{
    Task<ServiceResult<List<AnnouncementDto>>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends an announcement to the chosen members, in the app and by email.</summary>
    Task<ServiceResult<AnnouncementDto>> SendAsync(
        CreateAnnouncementRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class AnnouncementService : IAnnouncementService
{
    private readonly IGymAccessService _access;
    private readonly IRepository<Announcement> _announcements;
    private readonly IRepository<MemberProfile> _members;
    private readonly IRepository<MembershipPlan> _plans;
    private readonly IBillingEngine _gyms;
    private readonly INotificationService _notifications;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;

    public AnnouncementService(
        IGymAccessService access,
        IRepository<Announcement> announcements,
        IRepository<MemberProfile> members,
        IRepository<MembershipPlan> plans,
        IBillingEngine gyms,
        INotificationService notifications,
        IAuditService audit,
        TimeProvider clock)
    {
        _access = access;
        _announcements = announcements;
        _members = members;
        _plans = plans;
        _gyms = gyms;
        _notifications = notifications;
        _audit = audit;
        _clock = clock;
    }

    public async Task<ServiceResult<List<AnnouncementDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymMembersRead, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<List<AnnouncementDto>>(authorization);
        }

        var items = await _announcements.QueryAsync(
            q => q
                .OrderByDescending(x => x.SentAtUtc)
                .Take(100)
                .Select(x => new AnnouncementDto
                {
                    Id = x.Id,
                    Title = x.Title,
                    Body = x.Body,
                    Audience = x.Audience,
                    MembershipPlanId = x.MembershipPlanId,
                    SentBy = x.SentByName,
                    SentAtUtc = x.SentAtUtc,
                    RecipientCount = x.RecipientCount
                }),
            cancellationToken);

        return ServiceResult<List<AnnouncementDto>>.Ok(items);
    }

    public async Task<ServiceResult<AnnouncementDto>> SendAsync(
        CreateAnnouncementRequest request,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(
            Permissions.GymNotificationsSend,
            PlatformModules.GymEngagement,
            cancellationToken);

        if (!authorization.Allowed)
        {
            await _audit.RecordAsync(
                AuditCategory.Notification,
                "AnnouncementDenied",
                AuditOutcome.Denied,
                resourceType: "Announcement",
                reason: authorization.Reason,
                cancellationToken: cancellationToken);

            return GymResults.Forbidden<AnnouncementDto>(authorization);
        }

        if (request.Audience == AnnouncementAudience.Plan)
        {
            if (request.MembershipPlanId is not { } planId ||
                !await _plans.AnyAsync(x => x.Id == planId, cancellationToken))
            {
                return GymResults.Invalid<AnnouncementDto>("Choose the membership plan to announce to.");
            }
        }

        var recipients = await _members.QueryAsync(
            q =>
            {
                q = q.Where(x => x.AnonymizedAtUtc == null && x.Status != MemberStatus.Cancelled);

                q = request.Audience switch
                {
                    AnnouncementAudience.Active => q.Where(x => x.Status == MemberStatus.Active),
                    AnnouncementAudience.Overdue => q.Where(x => x.Invoices.Any(i => i.Status == InvoiceStatus.Overdue)),
                    AnnouncementAudience.Plan => q.Where(x => x.MembershipPlanId == request.MembershipPlanId),
                    _ => q
                };

                return q.Select(x => new NotificationRecipient(x.UserId, x.Email, x.FullName));
            },
            cancellationToken);

        if (recipients.Count == 0)
        {
            return GymResults.Invalid<AnnouncementDto>("No members match this audience.");
        }

        var tenantId = _access.TenantId!.Value;
        var gym = await _gyms.GetGymAsync(tenantId, cancellationToken);

        var announcement = new Announcement
        {
            TenantId = tenantId,
            Title = request.Title.Trim(),
            Body = request.Body.Trim(),
            Audience = request.Audience,
            MembershipPlanId = request.Audience == AnnouncementAudience.Plan ? request.MembershipPlanId : null,
            SentByUserId = _access.UserId!.Value,
            SentByName = _access.Username ?? "Staff",
            SentAtUtc = _clock.GetUtcNow().UtcDateTime,
            RecipientCount = recipients.Count
        };

        _announcements.Add(announcement);

        _notifications.Queue(
            tenantId,
            recipients,
            new NotificationMessage
            {
                Type = NotificationType.Announcement,
                Title = announcement.Title,
                Body = announcement.Body,
                Link = "/notifications",
                SendEmail = request.SendEmail,
                GymName = gym?.Name
            });

        await _announcements.SaveChangesAsync(cancellationToken);

        await _audit.RecordAsync(
            AuditCategory.Notification,
            "AnnouncementSent",
            AuditOutcome.Succeeded,
            resourceType: "Announcement",
            resourceId: announcement.Id.ToString(),
            reason: $"'{announcement.Title}' to {announcement.RecipientCount} member(s) ({announcement.Audience}).",
            cancellationToken: cancellationToken);

        return ServiceResult<AnnouncementDto>.Ok(new AnnouncementDto
        {
            Id = announcement.Id,
            Title = announcement.Title,
            Body = announcement.Body,
            Audience = announcement.Audience,
            MembershipPlanId = announcement.MembershipPlanId,
            SentBy = announcement.SentByName,
            SentAtUtc = announcement.SentAtUtc,
            RecipientCount = announcement.RecipientCount
        });
    }
}
