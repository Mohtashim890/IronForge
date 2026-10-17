using System.Net;
using System.Text;
using IronForge.Application.Commons;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.Persistence;
using IronForge.Shared.Models.Gym;

namespace IronForge.Application.Gym.Notifications;

/// <summary>Who receives a notification: in-app needs a user, email needs an address.</summary>
public sealed record NotificationRecipient(int? UserId, string? Email, string? Name);

public sealed class NotificationMessage
{
    public required NotificationType Type { get; init; }

    public required string Title { get; init; }

    /// <summary>Plain text; paragraphs separated by blank lines.</summary>
    public required string Body { get; init; }

    /// <summary>In-app route, e.g. "/invoices".</summary>
    public string? Link { get; init; }

    public bool SendEmail { get; init; }

    /// <summary>Gym name shown in the email header.</summary>
    public string? GymName { get; init; }

    /// <summary>Absolute URL for the email's button, e.g. an invitation or reset link.</summary>
    public string? EmailActionUrl { get; init; }

    public string? EmailActionText { get; init; }
}

public interface INotificationService
{
    Task<ServiceResult<List<NotificationDto>>> ListMineAsync(
        bool unreadOnly,
        int take,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<UnreadCountDto>> GetUnreadCountAsync(
        CancellationToken cancellationToken = default);

    Task<ServiceResult<bool>> MarkReadAsync(
        long notificationId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<int>> MarkAllReadAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds in-app notifications and outbox emails without saving: they are
    /// committed by the caller's next save, together with the change that
    /// caused them. The caller is responsible for authorization.
    /// </summary>
    void Queue(
        int? tenantId,
        IEnumerable<NotificationRecipient> recipients,
        NotificationMessage message);
}

public sealed class NotificationService : INotificationService
{
    private readonly IRepository<Notification> _notifications;
    private readonly IRepository<EmailOutboxMessage> _outbox;
    private readonly IExecutionContextAccessor _executionContext;
    private readonly TimeProvider _clock;

    public NotificationService(
        IRepository<Notification> notifications,
        IRepository<EmailOutboxMessage> outbox,
        IExecutionContextAccessor executionContext,
        TimeProvider clock)
    {
        _notifications = notifications;
        _outbox = outbox;
        _executionContext = executionContext;
        _clock = clock;
    }

    public async Task<ServiceResult<List<NotificationDto>>> ListMineAsync(
        bool unreadOnly,
        int take,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetInbox(out var tenantId, out var userId))
        {
            return NoInbox<List<NotificationDto>>();
        }

        take = Math.Clamp(take, 1, 100);

        var items = await _notifications.QueryAsync(
            q => q
                .Where(x => x.TenantId == tenantId && x.UserId == userId)
                .Where(x => !unreadOnly || x.ReadAtUtc == null)
                .OrderByDescending(x => x.CreatedAtUtc)
                .Take(take)
                .Select(x => new NotificationDto
                {
                    Id = x.Id,
                    Type = x.Type,
                    Title = x.Title,
                    Body = x.Body,
                    Link = x.Link,
                    CreatedAtUtc = x.CreatedAtUtc,
                    IsRead = x.ReadAtUtc != null
                }),
            cancellationToken);

        return ServiceResult<List<NotificationDto>>.Ok(items);
    }

    public async Task<ServiceResult<UnreadCountDto>> GetUnreadCountAsync(
        CancellationToken cancellationToken = default)
    {
        if (!TryGetInbox(out var tenantId, out var userId))
        {
            return NoInbox<UnreadCountDto>();
        }

        var count = await _notifications.CountAsync(
            x => x.TenantId == tenantId && x.UserId == userId && x.ReadAtUtc == null,
            cancellationToken);

        return ServiceResult<UnreadCountDto>.Ok(new UnreadCountDto { Count = count });
    }

    public async Task<ServiceResult<bool>> MarkReadAsync(
        long notificationId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetInbox(out var tenantId, out var userId))
        {
            return NoInbox<bool>();
        }

        var notification = await _notifications.FirstOrDefaultTrackedAsync(
            x => x.Id == notificationId && x.TenantId == tenantId && x.UserId == userId,
            cancellationToken);

        if (notification is null)
        {
            return ServiceResult<bool>.Fail("Notification was not found.", 404);
        }

        notification.ReadAtUtc ??= _clock.GetUtcNow().UtcDateTime;

        await _notifications.SaveChangesAsync(cancellationToken);

        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<int>> MarkAllReadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!TryGetInbox(out var tenantId, out var userId))
        {
            return NoInbox<int>();
        }

        var unread = await _notifications.ListTrackedAsync(
            x => x.TenantId == tenantId && x.UserId == userId && x.ReadAtUtc == null,
            cancellationToken);

        var now = _clock.GetUtcNow().UtcDateTime;

        foreach (var notification in unread)
        {
            notification.ReadAtUtc = now;
        }

        await _notifications.SaveChangesAsync(cancellationToken);

        return ServiceResult<int>.Ok(unread.Count);
    }

    public void Queue(
        int? tenantId,
        IEnumerable<NotificationRecipient> recipients,
        NotificationMessage message)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        string? html = null;

        foreach (var recipient in recipients)
        {
            if (tenantId.HasValue && recipient.UserId.HasValue)
            {
                _notifications.Add(new Notification
                {
                    TenantId = tenantId.Value,
                    UserId = recipient.UserId.Value,
                    Type = message.Type,
                    Title = Truncate(message.Title, 150),
                    Body = Truncate(message.Body, 4000),
                    Link = message.Link,
                    CreatedAtUtc = now
                });
            }

            if (message.SendEmail && !string.IsNullOrWhiteSpace(recipient.Email))
            {
                html ??= EmailTemplate.Render(
                    message.GymName,
                    message.Title,
                    message.Body,
                    message.EmailActionText,
                    message.EmailActionUrl);

                _outbox.Add(new EmailOutboxMessage
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ToEmail = recipient.Email.Trim(),
                    ToName = recipient.Name,
                    Subject = message.GymName is null
                        ? Truncate(message.Title, 300)
                        : Truncate($"{message.GymName}: {message.Title}", 300),
                    HtmlBody = html,
                    Status = EmailStatus.Pending,
                    CreatedAtUtc = now,
                    NextAttemptAtUtc = now
                });
            }
        }
    }

    private bool TryGetInbox(out int tenantId, out int userId)
    {
        var context = _executionContext.Current;

        tenantId = context?.TenantId ?? 0;
        userId = context?.Actor.UserId ?? 0;

        return context?.TenantId is not null && context.Actor.UserId is not null;
    }

    private static ServiceResult<T> NoInbox<T>() =>
        ServiceResult<T>.Fail("A gym must be selected.", 403);

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..(length - 1)] + "…";
}

/// <summary>A minimal, client-safe HTML email layout. All text is HTML-encoded.</summary>
public static class EmailTemplate
{
    public static string Render(
        string? gymName,
        string title,
        string body,
        string? actionText = null,
        string? actionUrl = null)
    {
        var html = new StringBuilder();

        html.Append("<!doctype html><html><body style=\"margin:0;padding:24px;background:#f4f5f7;font-family:Segoe UI,Arial,sans-serif;color:#1f2937\">");
        html.Append("<table role=\"presentation\" width=\"100%\" style=\"max-width:560px;margin:0 auto;background:#ffffff;border-radius:8px;padding:24px\">");
        html.Append("<tr><td>");
        html.Append("<p style=\"margin:0 0 4px;font-size:13px;color:#6b7280\">")
            .Append(WebUtility.HtmlEncode(gymName ?? "IronForge"))
            .Append("</p>");
        html.Append("<h1 style=\"margin:0 0 16px;font-size:20px\">")
            .Append(WebUtility.HtmlEncode(title))
            .Append("</h1>");

        foreach (var paragraph in body.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            html.Append("<p style=\"margin:0 0 12px;line-height:1.5\">")
                .Append(WebUtility.HtmlEncode(paragraph.Trim()).Replace("\n", "<br>"))
                .Append("</p>");
        }

        if (!string.IsNullOrWhiteSpace(actionUrl) &&
            Uri.TryCreate(actionUrl, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            html.Append("<p style=\"margin:20px 0\"><a href=\"")
                .Append(WebUtility.HtmlEncode(uri.ToString()))
                .Append("\" style=\"background:#d9480f;color:#ffffff;padding:10px 18px;border-radius:6px;text-decoration:none;display:inline-block\">")
                .Append(WebUtility.HtmlEncode(actionText ?? "Open"))
                .Append("</a></p>");
        }

        html.Append("<p style=\"margin:24px 0 0;font-size:12px;color:#9ca3af\">Sent by IronForge on behalf of your gym.</p>");
        html.Append("</td></tr></table></body></html>");

        return html.ToString();
    }
}
