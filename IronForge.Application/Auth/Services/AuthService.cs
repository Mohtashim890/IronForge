using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Auth.DTOs;
using IronForge.Application.Auth.Models;
using IronForge.Application.Configurations;
using IronForge.Application.Entities;
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

        public AuthService(
            IRepository<User> users,
            IRepository<RefreshToken> refreshTokens,
            IRepository<TenantMembership> tenantMemberships,
            IRepository<Tenant> tenants,
            IPasswordHasher<User> passwordHasher,
            IOptions<JwtSettings> jwtOptions,
            IRefreshTokenService refreshTokenService,
            IAuditService auditService)
        {
            _users = users;
            _refreshTokens = refreshTokens;
            _tenantMemberships = tenantMemberships;
            _tenants = tenants;
            _passwordHasher = passwordHasher;
            _jwtSettings = jwtOptions.Value;
            _refreshTokenService = refreshTokenService;
            _auditService = auditService;
        }

        public async Task<LoginResponse?> LoginAsync(LoginRequest request)
        {
            var user = await _users.FirstOrDefaultTrackedAsync(
                x => x.Username == request.Username);

            if (user is null || !user.IsActive)
            {
                await _auditService.RecordAsync(
                   AuditCategory.Authentication,
                   "AuthenticationFailed",
                   AuditOutcome.Failed,
                   actorType: AuditActorType.User,
                   reason: "Invalid credentials.");

                return null;
            }

            var passwordResult =
                _passwordHasher.VerifyHashedPassword(
                    user,
                    user.PasswordHash,
                    request.Password);

            if (passwordResult ==
                PasswordVerificationResult.Failed)
            {
                await _auditService.RecordAsync(
                   AuditCategory.Authentication,
                   "AuthenticationFailed",
                   AuditOutcome.Failed,
                   actorType: AuditActorType.User,
                   reason: "Invalid credentials.");

                return null;
            }

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

            // Generate new access token
            var newAccessToken =
                GenerateAccessToken(user, storedToken.TenantId);

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

            var membership =
                await _tenantMemberships.FirstOrDefaultAsync(
                    x =>
                        x.TenantId == tenantId &&
                        x.UserId == userId &&
                        x.IsActive,
                    cancellationToken);

            if (membership == null)
                return null;

            var tenant =
                await _tenants.FirstOrDefaultAsync(
                    x => x.Id == tenantId && x.IsActive,
                    cancellationToken);

            if (tenant == null)
                return null;

            var newAccessToken =
                GenerateAccessToken(
                    user,
                    tenantId);

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

        public async Task<string> CreateTenantAccessTokenAsync(
            int userId,
            int tenantId,
            CancellationToken cancellationToken = default)
        {
            var user = await _users.FirstOrDefaultAsync(
                x => x.Id == userId,
                cancellationToken);

            if (user == null)
                throw new InvalidOperationException(
                    "User was not found.");

            return GenerateAccessToken(
                user,
                tenantId);
        }

        private string GenerateAccessToken(User user, int? tenantId = null)
        {
            var claims = new List<Claim>
            {
                new Claim(
                    ClaimTypes.NameIdentifier,
                    user.Id.ToString()),

                new Claim(
                    ClaimTypes.Name,
                    user.Username),

                new Claim(
                    ClaimTypes.Role,
                    user.Role),

                new Claim(CustomClaimTypes.AgentId, "product-agent"),

                new Claim(CustomClaimTypes.ClientId, "ironforge-test-client")
            };

            if (tenantId.HasValue)
            {
                claims.Add(
                    new Claim(
                        CustomClaimTypes.TenantId,
                        tenantId.Value.ToString()));
            }

            var permissions =
                RolePermissions.GetPermissions(user.Role);

            foreach (var permission in permissions)
            {
                claims.Add(
                    new Claim(
                        "permission",
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

        public async Task<bool> RegisterAsync(
            RegisterRequest request)
        {
            var usernameExists =
                await _users.AnyAsync(
                    u => u.Username == request.Username);

            if (usernameExists)
            {
                return false;
            }

            var emailExists =
                await _users.AnyAsync(
                    u => u.Email == request.Email);

            if (emailExists)
            {
                return false;
            }

            var user = new User
            {
                Username = request.Username,
                Email = request.Email,
                Role = "User",
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            };

            user.PasswordHash =
                _passwordHasher.HashPassword(
                    user,
                    request.Password);

            _users.Add(user);

            await _users.SaveChangesAsync();

            return true;
        }
    }
}
