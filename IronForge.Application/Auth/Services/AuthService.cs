using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Auth.DTOs;
using IronForge.Application.Auth.Models;
using IronForge.Application.Commons;
using IronForge.Application.Configurations;
using IronForge.Application.Entities;
using IronForge.Application.Gym.Members;
using IronForge.Shared.Authorization;
using TwoFactorSetupDto = IronForge.Shared.Models.TwoFactorSetupDto;
using TwoFactorSignInRequest = IronForge.Shared.Models.TwoFactorSignInRequest;
using IronForge.Application.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace IronForge.Application.Auth.Services
{
    public class AuthService : IAuthService
    {
        private readonly IRepository<User> _users;
        private readonly IRepository<RefreshToken> _refreshTokens;
        private readonly IRepository<TenantMembership> _tenantMemberships;
        private readonly IRepository<Tenant> _tenants;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly JwtSettings _jwtSettings;
        private readonly IRefreshTokenService _refreshTokenService;
        private readonly IAuditService _auditService;
        private readonly IGymDirectory _gymDirectory;
        private readonly ISignInGuard _signInGuard;
        private readonly ITwoFactorService _twoFactor;

        /// <summary>How long the password step stays valid while the user finds their code.</summary>
        public static readonly TimeSpan TwoFactorChallengeLifetime = TimeSpan.FromMinutes(5);

        public AuthService(
            IRepository<User> users,
            IRepository<RefreshToken> refreshTokens,
            IRepository<TenantMembership> tenantMemberships,
            IRepository<Tenant> tenants,
            IPasswordHasher<User> passwordHasher,
            IOptions<JwtSettings> jwtOptions,
            IRefreshTokenService refreshTokenService,
            IAuditService auditService,
            IGymDirectory gymDirectory,
            ISignInGuard signInGuard,
            ITwoFactorService twoFactor)
        {
            _gymDirectory = gymDirectory;
            _signInGuard = signInGuard;
            _twoFactor = twoFactor;
            _users = users;
            _refreshTokens = refreshTokens;
            _tenantMemberships = tenantMemberships;
            _tenants = tenants;
            _passwordHasher = passwordHasher;
            _jwtSettings = jwtOptions.Value;
            _refreshTokenService = refreshTokenService;
            _auditService = auditService;
        }

        public async Task<LoginResponse?> LoginAsync(LoginRequest request) =>
            (await SignInAsync(request)).Response;

        public async Task<SignInOutcome> SignInAsync(LoginRequest request)
        {
            var login = (request.Username ?? "").Trim();
            var loginLower = login.ToLower();

            // Members usually remember their email rather than a username.
            var user = await _users.FirstOrDefaultTrackedAsync(
                x => x.Username == login || x.Email.ToLower() == loginLower);

            if (user is null || !user.IsActive)
            {
                await _auditService.RecordAsync(
                   AuditCategory.Authentication,
                   "AuthenticationFailed",
                   AuditOutcome.Failed,
                   actorType: AuditActorType.User,
                   reason: "Invalid credentials.");

                return SignInOutcome.InvalidCredentials;
            }

            // With two-factor still to come, a right password must not clear
            // the count of wrong codes.
            var secondStep =
                user.TwoFactorEnabled ||
                await _twoFactor.IsRequiredAsync(user);

            var passwordCheck =
                await _signInGuard.CheckPasswordAsync(
                    user.Id,
                    request.Password,
                    resetOnSuccess: !secondStep);

            if (passwordCheck == PasswordCheck.LockedOut)
            {
                await _auditService.RecordAsync(
                   AuditCategory.Authentication,
                   "AuthenticationLockedOut",
                   AuditOutcome.Denied,
                   actorType: AuditActorType.User,
                   resourceType: "User",
                   resourceId: user.Id.ToString(),
                   reason: "The account is locked after failed attempts.");

                return SignInOutcome.LockedOut(_signInGuard.LockedOutMessage(user));
            }

            if (passwordCheck == PasswordCheck.Invalid)
            {
                await _auditService.RecordAsync(
                   AuditCategory.Authentication,
                   "AuthenticationFailed",
                   AuditOutcome.Failed,
                   actorType: AuditActorType.User,
                   reason: "Invalid credentials.");

                return SignInOutcome.InvalidCredentials;
            }

            var denial = await GetSignInDenialAsync(user);

            if (denial is not null)
            {
                await _auditService.RecordAsync(
                   AuditCategory.Authentication,
                   "AuthenticationDenied",
                   AuditOutcome.Denied,
                   actorType: AuditActorType.User,
                   resourceType: "User",
                   resourceId: user.Id.ToString(),
                   reason: denial);

                return SignInOutcome.Denied(denial);
            }

            if (secondStep)
            {
                return SignInOutcome.Success(new LoginResponse
                {
                    TwoFactorRequired = true,
                    TwoFactorSetupRequired = !user.TwoFactorEnabled,
                    TwoFactorToken = GenerateTwoFactorChallenge(user)
                });
            }

            return SignInOutcome.Success(await StartSessionAsync(user));
        }

        /// <summary>
        /// Second sign-in step. With two-factor on, the code is checked;
        /// while it is being set up at sign-in, the first code confirms it and
        /// the recovery codes come back with the tokens.
        /// </summary>
        public async Task<SignInOutcome> CompleteTwoFactorSignInAsync(
            TwoFactorSignInRequest request,
            CancellationToken cancellationToken = default)
        {
            var user = await ReadTwoFactorChallengeAsync(request.TwoFactorToken, cancellationToken);

            if (user is null)
            {
                return SignInOutcome.Denied("Your sign-in expired. Sign in again.");
            }

            if (_signInGuard.IsLockedOut(user))
            {
                return SignInOutcome.LockedOut(_signInGuard.LockedOutMessage(user));
            }

            List<string>? recoveryCodes = null;
            bool valid;

            if (user.TwoFactorEnabled)
            {
                valid = _twoFactor.VerifyCode(user, request.Code);
            }
            else if (user.TwoFactorPendingSecret is not null)
            {
                recoveryCodes = _twoFactor.ConfirmSetup(user, request.Code);
                valid = recoveryCodes is not null;
            }
            else
            {
                return SignInOutcome.Denied("Set up your authenticator app first.");
            }

            if (!valid)
            {
                await _auditService.RecordAsync(
                    AuditCategory.Authentication,
                    "TwoFactorFailed",
                    AuditOutcome.Failed,
                    actorType: AuditActorType.User,
                    resourceType: "User",
                    resourceId: user.Id.ToString(),
                    reason: "Wrong two-factor code.",
                    cancellationToken: cancellationToken);

                return await _signInGuard.RecordFailureAsync(user, cancellationToken)
                    ? SignInOutcome.LockedOut(_signInGuard.LockedOutMessage(user))
                    : SignInOutcome.InvalidCode();
            }

            user.FailedSignInCount = 0;
            user.LockoutEndUtc = null;

            if (recoveryCodes is not null)
            {
                await _auditService.RecordAsync(
                    AuditCategory.Authentication,
                    "TwoFactorEnabled",
                    AuditOutcome.Succeeded,
                    actorType: AuditActorType.User,
                    resourceType: "User",
                    resourceId: user.Id.ToString(),
                    reason: "Set up at sign-in.",
                    cancellationToken: cancellationToken);
            }

            var response = await StartSessionAsync(user);
            response.RecoveryCodes = recoveryCodes;

            return SignInOutcome.Success(response);
        }

        /// <summary>Starts setting up the authenticator app for an account that must have one to sign in.</summary>
        public async Task<ServiceResult<TwoFactorSetupDto>> BeginTwoFactorSetupAtSignInAsync(
            string twoFactorToken,
            CancellationToken cancellationToken = default)
        {
            var user = await ReadTwoFactorChallengeAsync(twoFactorToken, cancellationToken);

            if (user is null)
            {
                return ServiceResult<TwoFactorSetupDto>.Fail("Your sign-in expired. Sign in again.", 403);
            }

            if (user.TwoFactorEnabled)
            {
                return ServiceResult<TwoFactorSetupDto>.Fail("Two-factor sign-in is already set up.", 409);
            }

            var setup = _twoFactor.StartSetup(user);
            await _users.SaveChangesAsync(cancellationToken);

            return ServiceResult<TwoFactorSetupDto>.Ok(setup);
        }

        /// <summary>
        /// A short-lived token proving the password step. Its audience differs
        /// from access tokens, so it cannot be used to call the API.
        /// </summary>
        private string GenerateTwoFactorChallenge(User user)
        {
            var token = new JwtSecurityToken(
                issuer: _jwtSettings.Issuer,
                audience: TwoFactorAudience,
                claims:
                [
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(CustomClaimTypes.SecurityStamp, user.SecurityStamp)
                ],
                expires: DateTime.UtcNow.Add(TwoFactorChallengeLifetime),
                signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Key)),
                    SecurityAlgorithms.HmacSha256));

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private string TwoFactorAudience => $"{_jwtSettings.Audience}.two-factor";

        private async Task<User?> ReadTwoFactorChallengeAsync(string? token, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(token))
                return null;

            ClaimsPrincipal principal;

            try
            {
                var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };

                principal = handler.ValidateToken(
                    token,
                    new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = _jwtSettings.Issuer,
                        ValidAudience = TwoFactorAudience,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Key)),
                        ClockSkew = TimeSpan.FromSeconds(30)
                    },
                    out _);
            }
            catch (Exception exception) when (exception is SecurityTokenException or ArgumentException)
            {
                return null;
            }

            if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                return null;

            var user = await _users.FirstOrDefaultTrackedAsync(x => x.Id == userId, cancellationToken);

            // A password reset in the meantime changes the stamp and voids the challenge.
            return user is { IsActive: true } &&
                   user.SecurityStamp == principal.FindFirstValue(CustomClaimTypes.SecurityStamp)
                ? user
                : null;
        }

        private async Task<LoginResponse> StartSessionAsync(User user)
        {
            user.LastLoginAtUtc = DateTime.UtcNow;

            var accessToken =
                GenerateAccessToken(user);
            var refreshToken =
                _refreshTokenService.GenerateToken();
            var refreshTokenHash =
                _refreshTokenService.HashToken(refreshToken);
            var familyId = Guid.NewGuid();

            var refreshTokenEntity = new RefreshToken
            {
                UserId = user.Id,
                TokenHash = refreshTokenHash,
                FamilyId = familyId,
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc =
                    DateTime.UtcNow.AddDays(30)
            };

            _refreshTokens.Add(refreshTokenEntity);
            //_users.Update(user);

            await _refreshTokens.SaveChangesAsync();

            await _auditService.RecordAsync(
                AuditCategory.Authentication,
                "AuthenticationSucceeded",
                AuditOutcome.Succeeded,
                actorType: AuditActorType.User,
                resourceType: "User",
                resourceId: user.Id.ToString());

            return new LoginResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken
            };
        }

        /// <summary>
        /// A gym member may sign in once staff approved them. An account whose
        /// only gym memberships are join requests (pending or declined) is
        /// refused with the reason. Operators and anyone with an active gym
        /// membership sign in normally.
        /// </summary>
        private async Task<string?> GetSignInDenialAsync(User user)
        {
            if (user.Role == PlatformRoles.PlatformAdmin)
                return null;

            if (await _tenantMemberships.AnyAsync(x => x.UserId == user.Id && x.IsActive))
                return null;

            var requests = await _gymDirectory.ListUnapprovedMembershipsAsync(user.Id);

            if (requests.FirstOrDefault(x => !x.Declined) is { } pending)
                return $"Your request to join {pending.GymName} is waiting for the gym's approval. " +
                       "You can sign in once it is approved.";

            if (requests.FirstOrDefault(x => x.Declined) is { } declined)
                return $"Your request to join {declined.GymName} was declined. " +
                       "Contact the gym, or join again with its code.";

            return null;
        }

        public async Task<LoginResponse?> RefreshTokenAsync(string refreshToken)
        {
            var refreshTokenHash =
                _refreshTokenService.HashToken(refreshToken);

            var storedToken =
                await _refreshTokens.FirstOrDefaultTrackedAsync(
                    x => x.TokenHash == refreshTokenHash);

            if (storedToken == null)
            {
                await _auditService.RecordAsync(
                    AuditCategory.Authentication,
                    "RefreshTokenRejected",
                    AuditOutcome.Failed,
                    actorType: AuditActorType.System,
                    reason: "Refresh token was not recognized.");

                return null;
            }

            var user = await _users.FirstOrDefaultAsync(
                x => x.Id == storedToken.UserId);

            if (user == null)
            {
                await _auditService.RecordAsync(
                    AuditCategory.Authentication,
                    "RefreshTokenRejected",
                    AuditOutcome.Failed,
                    actorType: AuditActorType.System,
                    reason: "Refresh token user was not found.");

                return null;
            }

            if (!user.IsActive)
            {
                await _auditService.RecordAsync(
                   AuditCategory.Authentication,
                   "RefreshTokenRejected",
                   AuditOutcome.Denied,
                   actorType: AuditActorType.User,
                   resourceType: "User",
                   resourceId: storedToken.UserId.ToString(),
                   reason: "User account is inactive.");

                return null;
            }

            // Token reuse detection
            if (storedToken.RevokedAtUtc != null)
            {
                await RevokeTokenFamilyAsync(
                    storedToken.FamilyId);

                await _auditService.RecordAsync(
                   AuditCategory.Authentication,
                   "RefreshTokenReuseDetected",
                   AuditOutcome.Denied,
                   actorType: AuditActorType.User,
                   resourceType: "RefreshTokenFamily",
                   resourceId: storedToken.FamilyId.ToString(),
                   reason: "A revoked refresh token was presented.");

                return null;
            }

            // Token expiration
            if (storedToken.ExpiresAtUtc <= DateTime.UtcNow)
            {
                await _auditService.RecordAsync(
                    AuditCategory.Authentication,
                    "RefreshTokenExpired",
                    AuditOutcome.Denied,
                    actorType: AuditActorType.User,
                    resourceType: "User",
                    resourceId: storedToken.UserId.ToString(),
                    reason: "Refresh token has expired.");

                return null;
            }

            // A tenant-bound session is only renewed while the user still
            // has access to that tenant. The role is re-read on every refresh,
            // so membership role changes apply from the next access token.
            string? tenantRole = null;

            if (storedToken.TenantId.HasValue)
            {
                var (membership, denialReason) =
                    await GetActiveTenantMembershipAsync(
                        user.Id,
                        storedToken.TenantId.Value);

                if (membership == null)
                {
                    // The whole session is bound to this tenant; end it.
                    await RevokeTokenFamilyAsync(
                        storedToken.FamilyId);

                    await _auditService.RecordAsync(
                        AuditCategory.Authentication,
                        "RefreshTokenRejected",
                        AuditOutcome.Denied,
                        actorType: AuditActorType.User,
                        resourceType: "Tenant",
                        resourceId: storedToken.TenantId.Value.ToString(),
                        reason: denialReason);

                    return null;
                }

                tenantRole = membership.Role;
            }

            // Generate new access token
            var newAccessToken =
                GenerateAccessToken(
                    user,
                    storedToken.TenantId,
                    tenantRole);

            // Generate new refresh token
            var newRefreshToken =
                _refreshTokenService.GenerateToken();

            var newRefreshTokenHash =
                _refreshTokenService.HashToken(
                    newRefreshToken);

            // Revoke current refresh token
            storedToken.RevokedAtUtc =
                DateTime.UtcNow;

            storedToken.ReplacedByTokenHash =
                newRefreshTokenHash;

            // Create replacement token
            var replacementToken =
                new RefreshToken
                {
                    UserId = storedToken.UserId,
                    TenantId = storedToken.TenantId,
                    FamilyId = storedToken.FamilyId,
                    TokenHash = newRefreshTokenHash,
                    CreatedAtUtc = DateTime.UtcNow,
                    ExpiresAtUtc =
                        DateTime.UtcNow.AddDays(30)
                };

            //_refreshTokens.Update(storedToken);
            _refreshTokens.Add(replacementToken);

            await _refreshTokens.SaveChangesAsync();

            return new LoginResponse
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshToken
            };
        }

        private async Task RevokeTokenFamilyAsync(Guid familyId)
        {
            var tokens =
                await _refreshTokens.ListTrackedAsync(
                    x => x.FamilyId == familyId);

            foreach (var token in tokens)
            {
                token.RevokedAtUtc =
                    DateTime.UtcNow;

                //_refreshTokens.Update(token);
            }

            await _refreshTokens.SaveChangesAsync();
        }

        public async Task<LoginResponse?> SelectTenantAsync(
            int userId,
            int tenantId,
            string refreshToken,
            CancellationToken cancellationToken = default)
        {
            var refreshTokenHash =
                _refreshTokenService.HashToken(refreshToken);

            var storedToken =
                await _refreshTokens.FirstOrDefaultTrackedAsync(
                    x => x.TokenHash == refreshTokenHash,
                    cancellationToken);

            if (storedToken == null)
                return null;

            if (storedToken.UserId != userId)
                return null;

            var user = await _users.FirstOrDefaultAsync(
                x => x.Id == storedToken.UserId,
                cancellationToken);

            if (user == null)
                return null;

            if (!user.IsActive)
                return null;

            if (storedToken.RevokedAtUtc != null)
                return null;

            if (storedToken.ExpiresAtUtc <= DateTime.UtcNow)
                return null;

            var (membership, _) =
                await GetActiveTenantMembershipAsync(
                    userId,
                    tenantId,
                    cancellationToken);

            if (membership == null)
                return null;

            var newAccessToken =
                GenerateAccessToken(
                    user,
                    tenantId,
                    membership.Role);

            var newRefreshToken =
                _refreshTokenService.GenerateToken();

            var newRefreshTokenHash =
                _refreshTokenService.HashToken(
                    newRefreshToken);

            storedToken.RevokedAtUtc =
                DateTime.UtcNow;

            storedToken.ReplacedByTokenHash =
                newRefreshTokenHash;

            var replacementToken =
                 new RefreshToken
                 {
                     UserId = storedToken.UserId,
                     TenantId = tenantId,
                     FamilyId = storedToken.FamilyId,
                     TokenHash = newRefreshTokenHash,
                     CreatedAtUtc = DateTime.UtcNow,
                     ExpiresAtUtc =
                         DateTime.UtcNow.AddDays(30)
                 };

            //_refreshTokens.Update(storedToken);
            _refreshTokens.Add(replacementToken);

            await _refreshTokens.SaveChangesAsync(
                cancellationToken);

            return new LoginResponse
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshToken
            };
        }

        /// <summary>
        /// Returns the user's membership when both the membership and the
        /// tenant are active; otherwise the reason access is denied.
        /// </summary>
        private async Task<(TenantMembership? Membership, string? DenialReason)>
            GetActiveTenantMembershipAsync(
                int userId,
                int tenantId,
                CancellationToken cancellationToken = default)
        {
            var membership =
                await _tenantMemberships.FirstOrDefaultAsync(
                    x =>
                        x.TenantId == tenantId &&
                        x.UserId == userId,
                    cancellationToken);

            if (membership == null)
                return (null, "User is not a member of the tenant.");

            if (!membership.IsActive)
                return (null, "Tenant membership is inactive.");

            var tenant =
                await _tenants.FirstOrDefaultAsync(
                    x => x.Id == tenantId,
                    cancellationToken);

            if (tenant == null)
                return (null, "Tenant no longer exists.");

            // Operator suspension and subscription state (trial, canceled).
            var now = DateTime.UtcNow;

            if (!tenant.HasAccess(now))
                return (null, tenant.DescribeNoAccess(now));

            return (membership, null);
        }

        /// <summary>
        /// Role and permissions are tenant-scoped: they come from the user's
        /// membership in the selected tenant (<paramref name="tenantRole"/>),
        /// never from the account-wide User.Role. A token without a tenant
        /// carries no role or permissions; it can only list and select tenants.
        /// </summary>
        private string GenerateAccessToken(
            User user,
            int? tenantId = null,
            string? tenantRole = null)
        {
            if (tenantId.HasValue &&
                string.IsNullOrWhiteSpace(tenantRole))
            {
                throw new InvalidOperationException(
                    "A tenant-scoped access token requires the tenant membership role.");
            }

            var claims = new List<Claim>
            {
                new Claim(
                    ClaimTypes.NameIdentifier,
                    user.Id.ToString()),

                new Claim(
                    ClaimTypes.Name,
                    user.Username),

                // Checked on every request (SessionValidator).
                new Claim(
                    CustomClaimTypes.SecurityStamp,
                    user.SecurityStamp)
            };

            if (tenantId.HasValue)
            {
                claims.Add(
                    new Claim(
                        CustomClaimTypes.TenantId,
                        tenantId.Value.ToString()));

                claims.Add(
                    new Claim(
                        ClaimTypes.Role,
                        tenantRole!));

                foreach (var permission in
                    RolePermissions.GetPermissions(tenantRole!))
                {
                    claims.Add(
                        new Claim(
                            CustomClaimTypes.Permission,
                            permission));
                }
            }

            // Platform permissions follow the account, not the tenant. They are
            // read from the database on every token, so a revoked platform role
            // disappears at the next refresh.
            foreach (var permission in
                RolePermissions.GetPlatformPermissions(user.Role))
            {
                claims.Add(
                    new Claim(
                        CustomClaimTypes.Permission,
                        permission));
            }

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_jwtSettings.Key));

            var credentials =
                new SigningCredentials(
                    key,
                    SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _jwtSettings.Issuer,
                audience: _jwtSettings.Audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(
                    _jwtSettings.ExpirationMinutes),
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler()
                .WriteToken(token);
        }
    }
}
