using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using IronForge.Application.Auth.DTOs;
using IronForge.Application.Auth.Models;
using IronForge.Application.Auth.Services;
using IronForge.Application.Auth.TwoFactor;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.IntegrationTests.Infrastructure;
using IronForge.Infrastructure.Data;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using LoginRequest = IronForge.Application.Auth.DTOs.LoginRequest;
using LoginResponse = IronForge.Application.Auth.DTOs.LoginResponse;
using TenantMembership = IronForge.Application.Entities.TenantMembership;

namespace IronForge.Application.IntegrationTests.Auth;

/// <summary>
/// Account lockout, two-factor sign-in and the per-request session check
/// against a real database. Every call uses a fresh DbContext (one request).
/// </summary>
public sealed class SignInSecurityTests
    : IntegrationTestBase,
      IClassFixture<PostgreSqlTestDatabase>
{
    private const string Password = "Correct-Horse-1";

    private readonly TestClock _clock = new(new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero));

    /// <summary>Shared like the API's singleton.</summary>
    private readonly SessionStateCache _sessions = new();

    public SignInSecurityTests(PostgreSqlTestDatabase database) : base(database)
    {
    }

    // ---- Lockout --------------------------------------------------------------

    [Fact]
    public async Task FiveWrongPasswords_LockTheAccount_EvenForTheRightPassword_UntilTheLockEnds()
    {
        var seed = await SeedAsync(TenantRoles.Trainer);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var wrong = await SignInAsync(seed.Username, "wrong-password");
            Assert.False(wrong.Succeeded);
            Assert.Null(wrong.DenialReason);
        }

        var fifth = await SignInAsync(seed.Username, "wrong-password");
        Assert.Equal(429, fifth.StatusCode);

        var locked = await SignInAsync(seed.Username, Password);
        Assert.Equal(429, locked.StatusCode);
        Assert.Contains("Try again in 1 minute", locked.DenialReason);

        _clock.Now += TimeSpan.FromMinutes(1.5);

        var after = await SignInAsync(seed.Username, Password);
        Assert.True(after.Succeeded);
        Assert.False(string.IsNullOrEmpty(after.Response!.AccessToken));

        var user = await LoadUserAsync(seed.UserId);
        Assert.Equal(0, user.FailedSignInCount);
        Assert.Null(user.LockoutEndUtc);
    }

    [Fact]
    public async Task EachFailureAfterTheLock_DoublesIt()
    {
        var seed = await SeedAsync(TenantRoles.Trainer);

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await SignInAsync(seed.Username, "wrong-password");
        }

        _clock.Now += TimeSpan.FromMinutes(2);
        await SignInAsync(seed.Username, "wrong-password");

        var user = await LoadUserAsync(seed.UserId);
        Assert.Equal(6, user.FailedSignInCount);
        Assert.Equal(_clock.Now.UtcDateTime.AddMinutes(2), user.LockoutEndUtc);
    }

    [Fact]
    public async Task PasswordChecksOutsideSignIn_CountTowardsTheLockout()
    {
        var seed = await SeedAsync(TenantRoles.Trainer);

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await using var db = CreateDbContext();
            var guard = Factory(db).SignInGuard;

            await guard.CheckPasswordAsync(seed.UserId, "wrong-password", resetOnSuccess: false);
        }

        Assert.Equal(429, (await SignInAsync(seed.Username, Password)).StatusCode);
    }

    // ---- Two-factor -------------------------------------------------------------

    [Fact]
    public async Task Admin_MustSetUpTwoFactor_AtFirstSignIn_ThenNeedsACodeEachTime()
    {
        var seed = await SeedAsync(TenantRoles.Admin);

        var first = await SignInAsync(seed.Username, Password, requireTwoFactor: true);

        Assert.True(first.Succeeded);
        Assert.True(first.Response!.TwoFactorRequired);
        Assert.True(first.Response.TwoFactorSetupRequired);
        Assert.Equal("", first.Response.AccessToken);
        Assert.Equal("", first.Response.RefreshToken);

        var (signedIn, secret) = await SetUpAtSignInAsync(first.Response);

        Assert.False(string.IsNullOrEmpty(signedIn.AccessToken));
        Assert.Equal(TwoFactorService.RecoveryCodeCount, signedIn.RecoveryCodes!.Count);

        var user = await LoadUserAsync(seed.UserId);
        Assert.True(user.TwoFactorEnabled);
        Assert.Null(user.TwoFactorPendingSecret);
        Assert.StartsWith("v1.", user.TwoFactorSecret);
        Assert.DoesNotContain(Totp.ToBase32(secret), user.TwoFactorSecret);
        Assert.DoesNotContain(signedIn.RecoveryCodes[0], user.TwoFactorRecoveryCodeHashes);

        // Next sign-in: a code, no setup.
        var second = await SignInAsync(seed.Username, Password, requireTwoFactor: true);
        Assert.True(second.Response!.TwoFactorRequired);
        Assert.False(second.Response.TwoFactorSetupRequired);

        // The code that set it up cannot be used again in the same 30 seconds.
        var replay = await CompleteAsync(second.Response.TwoFactorToken!, CurrentCode(secret));
        Assert.Equal(401, replay.StatusCode);

        _clock.Now += TimeSpan.FromSeconds(30);

        var withCode = await CompleteAsync(second.Response.TwoFactorToken!, CurrentCode(secret));
        Assert.True(withCode.Succeeded, withCode.DenialReason);
        Assert.Null(withCode.Response!.RecoveryCodes);
    }

    [Fact]
    public async Task RecoveryCode_SignsInOnce()
    {
        var seed = await SeedAsync(TenantRoles.Admin);
        var (signedIn, _) = await SetUpAtSignInAsync((await SignInAsync(seed.Username, Password, requireTwoFactor: true)).Response!);
        var recoveryCode = signedIn.RecoveryCodes![0].ToUpperInvariant();

        var challenge = (await SignInAsync(seed.Username, Password, requireTwoFactor: true)).Response!;

        Assert.True((await CompleteAsync(challenge.TwoFactorToken!, recoveryCode)).Succeeded);
        Assert.Equal(TwoFactorService.RecoveryCodeCount - 1, (await LoadUserAsync(seed.UserId)).TwoFactorRecoveryCodeHashes.Count);

        var again = (await SignInAsync(seed.Username, Password, requireTwoFactor: true)).Response!;

        Assert.Equal(401, (await CompleteAsync(again.TwoFactorToken!, recoveryCode)).StatusCode);
    }

    [Fact]
    public async Task WrongCodes_LockTheAccount_AndTheRightPasswordDoesNotResetTheCount()
    {
        var seed = await SeedAsync(TenantRoles.Admin);
        await SetUpAtSignInAsync((await SignInAsync(seed.Username, Password, requireTwoFactor: true)).Response!);

        var challenge = (await SignInAsync(seed.Username, Password, requireTwoFactor: true)).Response!;

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            Assert.Equal(401, (await CompleteAsync(challenge.TwoFactorToken!, "000000")).StatusCode);
        }

        // Signing in again with the password must not give four more guesses.
        var again = (await SignInAsync(seed.Username, Password, requireTwoFactor: true)).Response!;

        Assert.Equal(429, (await CompleteAsync(again.TwoFactorToken!, "000000")).StatusCode);
    }

    [Fact]
    public async Task Challenge_IsVoidAfterAPasswordReset_AndIsNotAnAccessToken()
    {
        var seed = await SeedAsync(TenantRoles.Admin);
        var challenge = (await SignInAsync(seed.Username, Password, requireTwoFactor: true)).Response!;

        // As an access token it fails: another audience.
        Assert.ThrowsAny<SecurityTokenException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(
                challenge.TwoFactorToken,
                new TokenValidationParameters
                {
                    ValidIssuer = "IronForge.Tests",
                    ValidAudience = "IronForge.Tests",
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestServiceFactory.JwtKey))
                },
                out _));

        await UpdateUserAsync(seed.UserId, x => x.SecurityStamp = User.NewSecurityStamp());

        await using var db = CreateDbContext();
        var setup = await Factory(db, requireTwoFactor: true).Auth.BeginTwoFactorSetupAtSignInAsync(challenge.TwoFactorToken!);

        Assert.Equal(403, setup.StatusCode);
    }

    [Fact]
    public async Task Trainer_IsNotForcedIntoTwoFactor_ButCanTurnItOnAndOff()
    {
        var seed = await SeedAsync(TenantRoles.Trainer);

        var signIn = await SignInAsync(seed.Username, Password, requireTwoFactor: true);
        Assert.False(signIn.Response!.TwoFactorRequired);

        var context = TestServiceFactory.AsRole(seed.TenantId, seed.UserId, TenantRoles.Trainer);

        var setup = await RunAsync(context, s => s.TwoFactor.BeginMySetupAsync());
        var secret = Totp.FromBase32(setup.Data!.ManualKey);

        var enabled = await RunAsync(context, s => s.TwoFactor.EnableMineAsync(new TwoFactorCodeRequest { Code = CurrentCode(secret) }));
        Assert.True(enabled.Success, enabled.ErrorMessage);

        // Now the trainer's sign-in asks for a code too.
        Assert.True((await SignInAsync(seed.Username, Password, requireTwoFactor: true)).Response!.TwoFactorRequired);

        _clock.Now += TimeSpan.FromSeconds(30);

        var disabled = await RunAsync(context, s => s.TwoFactor.DisableMineAsync(new TwoFactorCodeRequest { Code = CurrentCode(secret) }));
        Assert.True(disabled.Success, disabled.ErrorMessage);
        Assert.False((await LoadUserAsync(seed.UserId)).TwoFactorEnabled);
    }

    [Fact]
    public async Task Admin_CannotTurnTwoFactorOff()
    {
        var seed = await SeedAsync(TenantRoles.Admin);
        var (_, secret) = await SetUpAtSignInAsync((await SignInAsync(seed.Username, Password, requireTwoFactor: true)).Response!);

        _clock.Now += TimeSpan.FromSeconds(30);

        var context = TestServiceFactory.AsRole(seed.TenantId, seed.UserId, TenantRoles.Admin);

        var result = await RunAsync(
            context,
            s => s.TwoFactor.DisableMineAsync(new TwoFactorCodeRequest { Code = CurrentCode(secret) }),
            requireTwoFactor: true);

        Assert.Equal(409, result.StatusCode);
    }

    // ---- Every request is checked against the database ---------------------------

    [Fact]
    public async Task Session_StopsWorking_WhenStaffIsDeactivated_AndTheirRefreshTokensAreRevoked()
    {
        var seed = await SeedAsync(TenantRoles.Trainer, withSecondAdmin: true);
        var session = await SignInToGymAsync(seed);

        Assert.Null(await ValidateAsync(session.AccessToken));

        var adminContext = TestServiceFactory.AsRole(seed.TenantId, seed.SecondAdminId!.Value, TenantRoles.Admin);
        var deactivated = await RunAsync(adminContext, s => s.Members.UpdateStatusAsync(seed.UserId, isActive: false));
        Assert.True(deactivated.Success, deactivated.ErrorMessage);

        // At once on this instance: the change cleared the cached state.
        Assert.Equal("The gym membership is inactive.", await ValidateAsync(session.AccessToken));

        await using var db = CreateDbContext();
        Assert.All(
            await db.RefreshTokens.Where(x => x.UserId == seed.UserId && x.TenantId == seed.TenantId).ToListAsync(),
            x => Assert.NotNull(x.RevokedAtUtc));
    }

    [Fact]
    public async Task Session_StopsWorking_WhenTheRoleChanges_OrTheGymIsSuspended()
    {
        var seed = await SeedAsync(TenantRoles.Trainer);
        var session = await SignInToGymAsync(seed);

        await UpdateMembershipAsync(seed, x => x.Role = TenantRoles.Member);
        _clock.Now += SessionStateCache.Lifetime;

        Assert.Equal("The role in the gym changed.", await ValidateAsync(session.AccessToken));

        await UpdateMembershipAsync(seed, x => x.Role = TenantRoles.Trainer);

        await using (var db = CreateDbContext())
        {
            var tenant = await db.Tenants.SingleAsync(x => x.Id == seed.TenantId);
            tenant.IsActive = false;
            await db.SaveChangesAsync();
        }

        _clock.Now += SessionStateCache.Lifetime;

        Assert.Equal("The gym has no access to the platform.", await ValidateAsync(session.AccessToken));
    }

    [Fact]
    public async Task Session_IsCachedBriefly_ThenRechecked()
    {
        var seed = await SeedAsync(TenantRoles.Trainer);
        var session = await SignInToGymAsync(seed);

        Assert.Null(await ValidateAsync(session.AccessToken));

        // Changed by another API instance: this one notices when its cache entry expires.
        await UpdateMembershipAsync(seed, x => x.IsActive = false);

        Assert.Null(await ValidateAsync(session.AccessToken));

        _clock.Now += SessionStateCache.Lifetime;

        Assert.NotNull(await ValidateAsync(session.AccessToken));
    }

    [Fact]
    public async Task Session_StopsWorking_AfterTheSecurityStampChanges_OrWithoutOne()
    {
        var seed = await SeedAsync(TenantRoles.Trainer);
        var session = await SignInToGymAsync(seed);

        var claims = Read(session.AccessToken).Claims.ToList();
        var legacy = claims.Where(x => x.Type != CustomClaimTypes.SecurityStamp).ToList();

        Assert.Equal("The session ended (password or two-factor reset).", await ValidateAsync(legacy));

        await UpdateUserAsync(seed.UserId, x => x.SecurityStamp = User.NewSecurityStamp());
        _clock.Now += SessionStateCache.Lifetime;

        Assert.Equal("The session ended (password or two-factor reset).", await ValidateAsync(claims));
    }

    [Fact]
    public async Task PlatformPermission_StopsWorking_WhenTheOperatorRoleIsRemoved()
    {
        var seed = await SeedAsync(TenantRoles.Trainer, accountRole: PlatformRoles.PlatformAdmin);
        var login = (await SignInAsync(seed.Username, Password)).Response!;

        Assert.Null(await ValidateAsync(login.AccessToken));

        await UpdateUserAsync(seed.UserId, x => x.Role = PlatformRoles.User);
        _clock.Now += SessionStateCache.Lifetime;

        Assert.Equal("Platform access was removed.", await ValidateAsync(login.AccessToken));
    }

    // ---- Helpers ------------------------------------------------------------------

    private sealed record Seed(int UserId, string Username, int TenantId, int? SecondAdminId);

    private IronForgeDbContext CreateDbContext() =>
        Database.CreateDbContext(new IronForge.Application.Execution.ExecutionContext { Source = ExecutionSource.Web });

    private TestServiceFactory Factory(IronForgeDbContext db, bool requireTwoFactor = false) =>
        Factory(db, TestServiceFactory.Context(), requireTwoFactor);

    private TestServiceFactory Factory(
        IronForgeDbContext db,
        IronForge.Application.Execution.ExecutionContext context,
        bool requireTwoFactor = false) =>
        new(db, context)
        {
            Clock = _clock,
            SessionCache = _sessions,
            TwoFactorOptions = new TwoFactorOptions
            {
                EncryptionKey = "integration-test-two-factor-key-long-enough",
                RequiredForAdmins = requireTwoFactor
            }
        };

    private async Task<T> RunAsync<T>(
        IronForge.Application.Execution.ExecutionContext context,
        Func<TestServiceFactory, Task<T>> action,
        bool requireTwoFactor = false)
    {
        await using var db = Database.CreateDbContext(context);

        return await action(Factory(db, context, requireTwoFactor));
    }

    private async Task<Seed> SeedAsync(string tenantRole, bool withSecondAdmin = false, string accountRole = PlatformRoles.User)
    {
        await using var db = CreateDbContext();

        var now = _clock.Now.UtcDateTime;
        var tenant = new Tenant { Name = "Gym", Slug = $"gym-{Guid.NewGuid():N}", IsActive = true, CreatedAtUtc = now };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var user = await AddUserAsync(db, "staff-user", tenant.Id, tenantRole, accountRole);
        var secondAdmin = withSecondAdmin ? await AddUserAsync(db, "other-admin", tenant.Id, TenantRoles.Admin, PlatformRoles.User) : null;

        return new Seed(user.Id, user.Username, tenant.Id, secondAdmin?.Id);
    }

    private async Task<User> AddUserAsync(IronForgeDbContext db, string username, int tenantId, string tenantRole, string accountRole)
    {
        var now = _clock.Now.UtcDateTime;
        var user = new User
        {
            Username = username,
            Email = $"{username}@example.com",
            Role = accountRole,
            IsActive = true,
            CreatedAtUtc = now
        };

        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.TenantMemberships.Add(new TenantMembership
        {
            TenantId = tenantId,
            UserId = user.Id,
            Role = tenantRole,
            IsActive = true,
            CreatedAtUtc = now
        });

        await db.SaveChangesAsync();

        return user;
    }

    private async Task<SignInOutcome> SignInAsync(string username, string password, bool requireTwoFactor = false)
    {
        await using var db = CreateDbContext();

        return await Factory(db, requireTwoFactor).Auth.SignInAsync(new LoginRequest { Username = username, Password = password });
    }

    private async Task<SignInOutcome> CompleteAsync(string twoFactorToken, string code)
    {
        await using var db = CreateDbContext();

        return await Factory(db, requireTwoFactor: true).Auth.CompleteTwoFactorSignInAsync(
            new TwoFactorSignInRequest { TwoFactorToken = twoFactorToken, Code = code });
    }

    private async Task<(LoginResponse Response, byte[] Secret)> SetUpAtSignInAsync(LoginResponse challenge)
    {
        TwoFactorSetupDto setup;

        await using (var db = CreateDbContext())
        {
            var started = await Factory(db, requireTwoFactor: true).Auth.BeginTwoFactorSetupAtSignInAsync(challenge.TwoFactorToken!);
            Assert.True(started.Success, started.ErrorMessage);
            setup = started.Data!;
        }

        var secret = Totp.FromBase32(setup.ManualKey);
        var completed = await CompleteAsync(challenge.TwoFactorToken!, CurrentCode(secret));

        Assert.True(completed.Succeeded, completed.DenialReason);

        return (completed.Response!, secret);
    }

    private string CurrentCode(byte[] secret) => Totp.Code(secret, Totp.StepAt(_clock.Now));

    private async Task<LoginResponse> SignInToGymAsync(Seed seed)
    {
        var login = (await SignInAsync(seed.Username, Password)).Response!;

        await using var db = CreateDbContext();

        var selected = await Factory(db).Auth.SelectTenantAsync(seed.UserId, seed.TenantId, login.RefreshToken);

        Assert.NotNull(selected);

        return selected!;
    }

    private Task<string?> ValidateAsync(string accessToken) => ValidateAsync(Read(accessToken).Claims);

    private async Task<string?> ValidateAsync(IEnumerable<Claim> claims)
    {
        await using var db = CreateDbContext();

        return await Factory(db).Sessions.ValidateAsync(new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")));
    }

    private async Task<User> LoadUserAsync(int userId)
    {
        await using var db = CreateDbContext();

        return await db.Users.SingleAsync(x => x.Id == userId);
    }

    private async Task UpdateUserAsync(int userId, Action<User> update)
    {
        await using var db = CreateDbContext();

        update(await db.Users.SingleAsync(x => x.Id == userId));
        await db.SaveChangesAsync();
    }

    private async Task UpdateMembershipAsync(Seed seed, Action<TenantMembership> update)
    {
        await using var db = CreateDbContext();

        update(await db.TenantMemberships.SingleAsync(x => x.UserId == seed.UserId && x.TenantId == seed.TenantId));
        await db.SaveChangesAsync();
    }

    private static JwtSecurityToken Read(string accessToken) => new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
}
