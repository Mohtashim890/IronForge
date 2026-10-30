using System.Security.Cryptography;
using System.Text;
using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Auth.TwoFactor;
using IronForge.Application.Commons;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.Persistence;
using IronForge.Shared.Authorization;
using Microsoft.Extensions.Options;
using TwoFactorCodeRequest = IronForge.Shared.Models.TwoFactorCodeRequest;
using TwoFactorRecoveryCodesDto = IronForge.Shared.Models.TwoFactorRecoveryCodesDto;
using TwoFactorSetupDto = IronForge.Shared.Models.TwoFactorSetupDto;
using TwoFactorStatusDto = IronForge.Shared.Models.TwoFactorStatusDto;

namespace IronForge.Application.Auth.Services;

/// <summary>
/// Two-factor sign-in with an authenticator app (TOTP) and one-time recovery
/// codes. The sign-in steps (AuthService) use the user-level methods; the
/// signed-in user manages their own two-factor with the others.
/// </summary>
public interface ITwoFactorService
{
    /// <summary>
    /// Only platform operators must use two-factor (TwoFactor:RequiredForPlatformAdmins);
    /// for gym admins, trainers and members it is their own choice.
    /// </summary>
    Task<bool> IsRequiredAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>Stores a new pending secret on the tracked user (the caller saves).</summary>
    TwoFactorSetupDto StartSetup(User trackedUser);

    /// <summary>
    /// Turns two-factor on when the code matches the pending secret, and
    /// returns new recovery codes; null for a wrong code. The caller saves.
    /// </summary>
    List<string>? ConfirmSetup(User trackedUser, string code);

    /// <summary>A current authenticator code or an unused recovery code (used up). The caller saves.</summary>
    bool VerifyCode(User trackedUser, string code);

    Task<ServiceResult<TwoFactorStatusDto>> GetMyStatusAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult<TwoFactorSetupDto>> BeginMySetupAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult<TwoFactorRecoveryCodesDto>> EnableMineAsync(TwoFactorCodeRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<TwoFactorRecoveryCodesDto>> RegenerateMyRecoveryCodesAsync(TwoFactorCodeRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<bool>> DisableMineAsync(TwoFactorCodeRequest request, CancellationToken cancellationToken = default);
}

public sealed class TwoFactorService : ITwoFactorService
{
    public const int RecoveryCodeCount = 10;

    private const string RecoveryAlphabet = "abcdefghjkmnpqrstuvwxyz23456789";

    private readonly IRepository<User> _users;
    private readonly ITwoFactorSecretProtector _protector;
    private readonly ISignInGuard _guard;
    private readonly IExecutionContextAccessor _context;
    private readonly IAuditService _audit;
    private readonly TwoFactorOptions _options;
    private readonly TimeProvider _clock;

    public TwoFactorService(
        IRepository<User> users,
        ITwoFactorSecretProtector protector,
        ISignInGuard guard,
        IExecutionContextAccessor context,
        IAuditService audit,
        IOptions<TwoFactorOptions> options,
        TimeProvider clock)
    {
        _users = users;
        _protector = protector;
        _guard = guard;
        _context = context;
        _audit = audit;
        _options = options.Value;
        _clock = clock;
    }

    public Task<bool> IsRequiredAsync(User user, CancellationToken cancellationToken = default) =>
        Task.FromResult(_options.RequiredForPlatformAdmins && user.Role == PlatformRoles.PlatformAdmin);

    public TwoFactorSetupDto StartSetup(User trackedUser)
    {
        var secret = Totp.NewSecret();
        trackedUser.TwoFactorPendingSecret = _protector.Protect(secret);

        var key = Totp.ToBase32(secret);

        return new TwoFactorSetupDto
        {
            ManualKey = string.Join(' ', key.Chunk(4).Select(x => new string(x))),
            OtpAuthUri = Totp.OtpAuthUri(_options.Issuer, trackedUser.Email, secret)
        };
    }

    public List<string>? ConfirmSetup(User trackedUser, string code)
    {
        if (trackedUser.TwoFactorPendingSecret is null)
        {
            return null;
        }

        var secret = _protector.Unprotect(trackedUser.TwoFactorPendingSecret);
        var step = Totp.Match(secret, code, _clock.GetUtcNow(), lastUsedStep: null);

        if (step is null)
        {
            return null;
        }

        trackedUser.TwoFactorSecret = trackedUser.TwoFactorPendingSecret;
        trackedUser.TwoFactorPendingSecret = null;
        trackedUser.TwoFactorEnabled = true;
        trackedUser.TwoFactorEnabledAtUtc = _clock.GetUtcNow().UtcDateTime;
        trackedUser.TwoFactorLastStep = step;

        return NewRecoveryCodes(trackedUser);
    }

    public bool VerifyCode(User trackedUser, string code)
    {
        if (!trackedUser.TwoFactorEnabled || trackedUser.TwoFactorSecret is null)
        {
            return false;
        }

        var normalized = Totp.Normalize(code).ToLowerInvariant();

        if (normalized.Length == Totp.Digits)
        {
            var secret = _protector.Unprotect(trackedUser.TwoFactorSecret);
            var step = Totp.Match(secret, normalized, _clock.GetUtcNow(), trackedUser.TwoFactorLastStep);

            if (step is null)
            {
                return false;
            }

            trackedUser.TwoFactorLastStep = step;
            return true;
        }

        var hash = HashRecoveryCode(normalized);

        if (!trackedUser.TwoFactorRecoveryCodeHashes.Contains(hash))
        {
            return false;
        }

        // A new list, so EF sees the change of the array column.
        trackedUser.TwoFactorRecoveryCodeHashes = trackedUser.TwoFactorRecoveryCodeHashes.Where(x => x != hash).ToList();
        return true;
    }

    public async Task<ServiceResult<TwoFactorStatusDto>> GetMyStatusAsync(CancellationToken cancellationToken = default)
    {
        var user = await CurrentUserAsync(cancellationToken);

        if (user is null)
        {
            return ServiceResult<TwoFactorStatusDto>.Fail("Sign in to manage two-factor sign-in.", 401);
        }

        return ServiceResult<TwoFactorStatusDto>.Ok(new TwoFactorStatusDto
        {
            Enabled = user.TwoFactorEnabled,
            Required = await IsRequiredAsync(user, cancellationToken),
            EnabledAtUtc = user.TwoFactorEnabledAtUtc,
            RecoveryCodesLeft = user.TwoFactorRecoveryCodeHashes.Count
        });
    }

    public async Task<ServiceResult<TwoFactorSetupDto>> BeginMySetupAsync(CancellationToken cancellationToken = default)
    {
        var user = await CurrentUserAsync(cancellationToken);

        if (user is null)
        {
            return ServiceResult<TwoFactorSetupDto>.Fail("Sign in to manage two-factor sign-in.", 401);
        }

        if (user.TwoFactorEnabled)
        {
            return ServiceResult<TwoFactorSetupDto>.Fail("Two-factor sign-in is already on.", 409);
        }

        var setup = StartSetup(user);
        await _users.SaveChangesAsync(cancellationToken);

        return ServiceResult<TwoFactorSetupDto>.Ok(setup);
    }

    public async Task<ServiceResult<TwoFactorRecoveryCodesDto>> EnableMineAsync(
        TwoFactorCodeRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await CurrentUserAsync(cancellationToken);

        if (user is null)
        {
            return ServiceResult<TwoFactorRecoveryCodesDto>.Fail("Sign in to manage two-factor sign-in.", 401);
        }

        if (user.TwoFactorEnabled)
        {
            return ServiceResult<TwoFactorRecoveryCodesDto>.Fail("Two-factor sign-in is already on.", 409);
        }

        if (_guard.IsLockedOut(user))
        {
            return ServiceResult<TwoFactorRecoveryCodesDto>.Fail(_guard.LockedOutMessage(user), 429);
        }

        var codes = ConfirmSetup(user, request.Code);

        if (codes is null)
        {
            await _guard.RecordFailureAsync(user, cancellationToken);
            return ServiceResult<TwoFactorRecoveryCodesDto>.Fail(
                "That code is not valid. Check the time on your phone and enter the current code.", 400);
        }

        await _users.SaveChangesAsync(cancellationToken);
        await AuditAsync(user, "TwoFactorEnabled", cancellationToken);

        return ServiceResult<TwoFactorRecoveryCodesDto>.Ok(new TwoFactorRecoveryCodesDto { RecoveryCodes = codes });
    }

    public async Task<ServiceResult<TwoFactorRecoveryCodesDto>> RegenerateMyRecoveryCodesAsync(
        TwoFactorCodeRequest request,
        CancellationToken cancellationToken = default)
    {
        var (user, failure) = await CurrentUserWithCodeAsync<TwoFactorRecoveryCodesDto>(request.Code, cancellationToken);

        if (user is null)
        {
            return failure!;
        }

        var codes = NewRecoveryCodes(user);
        await _users.SaveChangesAsync(cancellationToken);
        await AuditAsync(user, "TwoFactorRecoveryCodesRegenerated", cancellationToken);

        return ServiceResult<TwoFactorRecoveryCodesDto>.Ok(new TwoFactorRecoveryCodesDto { RecoveryCodes = codes });
    }

    public async Task<ServiceResult<bool>> DisableMineAsync(
        TwoFactorCodeRequest request,
        CancellationToken cancellationToken = default)
    {
        var (user, failure) = await CurrentUserWithCodeAsync<bool>(request.Code, cancellationToken);

        if (user is null)
        {
            return failure!;
        }

        if (await IsRequiredAsync(user, cancellationToken))
        {
            return ServiceResult<bool>.Fail("Platform operators must keep two-factor sign-in on.", 409);
        }

        ClearTwoFactor(user);
        await _users.SaveChangesAsync(cancellationToken);
        await AuditAsync(user, "TwoFactorDisabled", cancellationToken);

        return ServiceResult<bool>.Ok(true);
    }

    /// <summary>Removes the authenticator and recovery codes (also used by the operator reset).</summary>
    public static void ClearTwoFactor(User user)
    {
        user.TwoFactorEnabled = false;
        user.TwoFactorSecret = null;
        user.TwoFactorPendingSecret = null;
        user.TwoFactorLastStep = null;
        user.TwoFactorEnabledAtUtc = null;
        user.TwoFactorRecoveryCodeHashes = [];
    }

    public static string HashRecoveryCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Totp.Normalize(code).ToLowerInvariant())));

    private static List<string> NewRecoveryCodesPlain() =>
        Enumerable.Range(0, RecoveryCodeCount)
            .Select(_ =>
            {
                var chars = RandomNumberGenerator.GetItems<char>(RecoveryAlphabet, 10);
                return $"{new string(chars, 0, 5)}-{new string(chars, 5, 5)}";
            })
            .ToList();

    private static List<string> NewRecoveryCodes(User trackedUser)
    {
        var codes = NewRecoveryCodesPlain();
        trackedUser.TwoFactorRecoveryCodeHashes = codes.Select(HashRecoveryCode).ToList();
        return codes;
    }

    private async Task<(User? User, ServiceResult<T>? Failure)> CurrentUserWithCodeAsync<T>(
        string code,
        CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(cancellationToken);

        if (user is null)
        {
            return (null, ServiceResult<T>.Fail("Sign in to manage two-factor sign-in.", 401));
        }

        if (!user.TwoFactorEnabled)
        {
            return (null, ServiceResult<T>.Fail("Two-factor sign-in is off.", 409));
        }

        if (_guard.IsLockedOut(user))
        {
            return (null, ServiceResult<T>.Fail(_guard.LockedOutMessage(user), 429));
        }

        if (!VerifyCode(user, code))
        {
            await _guard.RecordFailureAsync(user, cancellationToken);
            return (null, ServiceResult<T>.Fail("That code is not valid.", 400));
        }

        return (user, null);
    }

    /// <summary>The signed-in person; never an agent or a device acting for them.</summary>
    private async Task<User?> CurrentUserAsync(CancellationToken cancellationToken)
    {
        var actor = _context.Current?.Actor;

        if (actor?.UserId is not { } userId || actor.Agent is not null || actor.DeviceId is not null)
        {
            return null;
        }

        return await _users.FirstOrDefaultTrackedAsync(x => x.Id == userId && x.IsActive, cancellationToken);
    }

    private Task AuditAsync(User user, string action, CancellationToken cancellationToken) =>
        _audit.RecordAsync(
            AuditCategory.Authentication,
            action,
            AuditOutcome.Succeeded,
            actorType: AuditActorType.User,
            resourceType: "User",
            resourceId: user.Id.ToString(),
            cancellationToken: cancellationToken);
}
