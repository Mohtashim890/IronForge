using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Entities;
using IronForge.Application.Persistence;
using Microsoft.AspNetCore.Identity;

namespace IronForge.Application.Auth.Services;

public enum PasswordCheck
{
    Valid,
    Invalid,
    LockedOut
}

/// <summary>
/// Account lockout for every place a password or two-factor code is checked
/// (sign-in, joining a gym with an existing account, accepting an invitation).
/// The rate limits are per IP address; this is per account, so spreading
/// guesses over many addresses does not help.
/// </summary>
public interface ISignInGuard
{
    /// <summary>
    /// Checks the password unless the account is locked. A wrong password
    /// counts as a failure. <paramref name="resetOnSuccess"/> clears the
    /// failures; pass false while a two-factor code is still to come, so the
    /// password alone cannot reset the count of wrong codes.
    /// </summary>
    Task<PasswordCheck> CheckPasswordAsync(
        int userId,
        string password,
        bool resetOnSuccess,
        CancellationToken cancellationToken = default);

    /// <summary>Counts a failure (a wrong two-factor code); true when the account is now locked.</summary>
    Task<bool> RecordFailureAsync(User trackedUser, CancellationToken cancellationToken = default);

    /// <summary>Clears the failures after a complete sign-in.</summary>
    Task ResetAsync(User trackedUser, CancellationToken cancellationToken = default);

    bool IsLockedOut(User user);

    string LockedOutMessage(User user);
}

public sealed class SignInGuard : ISignInGuard
{
    /// <summary>Failures in a row before the first lock.</summary>
    public const int FreeAttempts = 5;

    public static readonly TimeSpan MaximumLockout = TimeSpan.FromMinutes(30);

    public const string GenericLockedOutMessage =
        "Too many failed sign-in attempts for this account. Try again later, or reset your password.";

    private readonly IRepository<User> _users;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;

    public SignInGuard(
        IRepository<User> users,
        IPasswordHasher<User> passwordHasher,
        IAuditService audit,
        TimeProvider clock)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _audit = audit;
        _clock = clock;
    }

    /// <summary>1, 2, 4, 8, 16 minutes, then 30, for each failure from the fifth on.</summary>
    public static TimeSpan LockoutFor(int failures) =>
        failures < FreeAttempts
            ? TimeSpan.Zero
            : TimeSpan.FromMinutes(Math.Min(MaximumLockout.TotalMinutes, Math.Pow(2, Math.Min(failures - FreeAttempts, 10))));

    public async Task<PasswordCheck> CheckPasswordAsync(
        int userId,
        string password,
        bool resetOnSuccess,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.FirstOrDefaultTrackedAsync(x => x.Id == userId, cancellationToken);

        if (user is null)
        {
            return PasswordCheck.Invalid;
        }

        if (IsLockedOut(user))
        {
            return PasswordCheck.LockedOut;
        }

        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password ?? "");

        if (result == PasswordVerificationResult.Failed)
        {
            return await RecordFailureAsync(user, cancellationToken)
                ? PasswordCheck.LockedOut
                : PasswordCheck.Invalid;
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, password!);
        }

        if (resetOnSuccess)
        {
            user.FailedSignInCount = 0;
            user.LockoutEndUtc = null;
        }

        await _users.SaveChangesAsync(cancellationToken);

        return PasswordCheck.Valid;
    }

    public async Task<bool> RecordFailureAsync(User trackedUser, CancellationToken cancellationToken = default)
    {
        trackedUser.FailedSignInCount++;

        var lockout = LockoutFor(trackedUser.FailedSignInCount);

        if (lockout > TimeSpan.Zero)
        {
            trackedUser.LockoutEndUtc = _clock.GetUtcNow().UtcDateTime + lockout;
        }

        await _users.SaveChangesAsync(cancellationToken);

        if (lockout > TimeSpan.Zero)
        {
            await _audit.RecordAsync(
                AuditCategory.Authentication,
                "AccountLockedOut",
                AuditOutcome.Denied,
                actorType: AuditActorType.System,
                resourceType: "User",
                resourceId: trackedUser.Id.ToString(),
                reason: $"{trackedUser.FailedSignInCount} failed sign-in attempts in a row; locked for {lockout.TotalMinutes:0} minute(s).",
                cancellationToken: cancellationToken);
        }

        return lockout > TimeSpan.Zero;
    }

    public async Task ResetAsync(User trackedUser, CancellationToken cancellationToken = default)
    {
        if (trackedUser.FailedSignInCount == 0 && trackedUser.LockoutEndUtc is null)
        {
            return;
        }

        trackedUser.FailedSignInCount = 0;
        trackedUser.LockoutEndUtc = null;

        await _users.SaveChangesAsync(cancellationToken);
    }

    public bool IsLockedOut(User user) =>
        user.LockoutEndUtc is { } end && end > _clock.GetUtcNow().UtcDateTime;

    public string LockedOutMessage(User user)
    {
        var minutes = user.LockoutEndUtc is { } end
            ? Math.Max(1, (int)Math.Ceiling((end - _clock.GetUtcNow().UtcDateTime).TotalMinutes))
            : 1;

        return $"Too many failed sign-in attempts. Try again in {minutes} minute{(minutes == 1 ? "" : "s")}, or reset your password.";
    }
}
