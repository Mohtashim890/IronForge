using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Commons;
using IronForge.Application.Configurations;
using IronForge.Application.Entities;
using IronForge.Application.Gym.Notifications;
using IronForge.Application.Persistence;
using IronForge.Application.Tenants.Invitations;
using IronForge.Shared.Models.Gym;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace IronForge.Application.Auth.Services;

public interface IPasswordResetService
{
    /// <summary>
    /// Emails a reset link when the address has an active account. Always
    /// succeeds, so the response does not reveal which emails have accounts.
    /// </summary>
    Task RequestAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Sets a new password and signs the user out everywhere.</summary>
    Task<ServiceResult<bool>> ResetAsync(
        string token,
        string newPassword,
        CancellationToken cancellationToken = default);
}

public sealed class PasswordResetService : IPasswordResetService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    private readonly IRepository<User> _users;
    private readonly IRepository<PasswordResetToken> _tokens;
    private readonly IRepository<RefreshToken> _refreshTokens;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly INotificationService _notifications;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;
    private readonly AppUrlOptions _urls;

    public PasswordResetService(
        IRepository<User> users,
        IRepository<PasswordResetToken> tokens,
        IRepository<RefreshToken> refreshTokens,
        IPasswordHasher<User> passwordHasher,
        INotificationService notifications,
        IAuditService audit,
        TimeProvider clock,
        IOptions<AppUrlOptions> urls)
    {
        _users = users;
        _tokens = tokens;
        _refreshTokens = refreshTokens;
        _passwordHasher = passwordHasher;
        _notifications = notifications;
        _audit = audit;
        _clock = clock;
        _urls = urls.Value;
    }

    public async Task RequestAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalized = InvitationToken.NormalizeEmail(email ?? "");

        var user = await _users.FirstOrDefaultAsync(
            x => x.Email.ToLower() == normalized && x.IsActive,
            cancellationToken);

        if (user is null)
        {
            return;
        }

        var now = _clock.GetUtcNow().UtcDateTime;

        // Only the newest link works.
        foreach (var open in await _tokens.ListTrackedAsync(
                     x => x.UserId == user.Id && x.UsedAtUtc == null && x.ExpiresAtUtc > now,
                     cancellationToken))
        {
            open.UsedAtUtc = now;
        }

        var token = InvitationToken.Create();

        _tokens.Add(new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = InvitationToken.Hash(token),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(Lifetime)
        });

        var separator = _urls.ResetPasswordUrl.Contains('?') ? "&" : "?";

        _notifications.Queue(
            tenantId: null,
            [new NotificationRecipient(null, user.Email, user.Username)],
            new NotificationMessage
            {
                Type = NotificationType.System,
                Title = "Reset your IronForge password",
                Body =
                    $"Hi {user.Username}, we received a request to reset your password.\n\n" +
                    "The link below works for one hour. If you did not ask for this, ignore this email.",
                SendEmail = true,
                EmailActionText = "Choose a new password",
                EmailActionUrl = $"{_urls.ResetPasswordUrl}{separator}token={Uri.EscapeDataString(token)}"
            });

        await _tokens.SaveChangesAsync(cancellationToken);

        await _audit.RecordAsync(
            AuditCategory.Authentication,
            "PasswordResetRequested",
            AuditOutcome.Succeeded,
            AuditActorType.User,
            "User",
            user.Id.ToString(),
            cancellationToken: cancellationToken);
    }

    public async Task<ServiceResult<bool>> ResetAsync(
        string token,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrEmpty(newPassword) || newPassword.Length < 8)
        {
            return ServiceResult<bool>.Fail("Choose a password of at least 8 characters.", 400);
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var hash = InvitationToken.Hash(token);

        var reset = await _tokens.FirstOrDefaultTrackedAsync(x => x.TokenHash == hash, cancellationToken);

        if (reset is null || reset.UsedAtUtc != null || reset.ExpiresAtUtc <= now)
        {
            return ServiceResult<bool>.Fail("This reset link is invalid or has expired. Request a new one.", 400);
        }

        var user = await _users.FirstOrDefaultTrackedAsync(x => x.Id == reset.UserId && x.IsActive, cancellationToken);

        if (user is null)
        {
            return ServiceResult<bool>.Fail("This reset link is invalid or has expired. Request a new one.", 400);
        }

        user.PasswordHash = _passwordHasher.HashPassword(user, newPassword);
        reset.UsedAtUtc = now;

        // Access tokens die too (SessionValidator), and the lockout is lifted.
        user.SecurityStamp = User.NewSecurityStamp();
        user.FailedSignInCount = 0;
        user.LockoutEndUtc = null;

        // A reset usually means the password leaked: end every session.
        foreach (var refresh in await _refreshTokens.ListTrackedAsync(
                     x => x.UserId == user.Id && x.RevokedAtUtc == null,
                     cancellationToken))
        {
            refresh.RevokedAtUtc = now;
        }

        await _users.SaveChangesAsync(cancellationToken);

        await _audit.RecordAsync(
            AuditCategory.Authentication,
            "PasswordReset",
            AuditOutcome.Succeeded,
            AuditActorType.User,
            "User",
            user.Id.ToString(),
            cancellationToken: cancellationToken);

        return ServiceResult<bool>.Ok(true);
    }
}
