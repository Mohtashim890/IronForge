using IronForge.Application.Auditing.Services;
using IronForge.Application.Auth.DTOs;
using IronForge.Application.Auth.Models;
using IronForge.Application.Auth.Services;
using IronForge.Application.Configurations;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.IntegrationTests.Infrastructure;
using IronForge.Infrastructure.Data;
using IronForge.Infrastructure.Persistence;
using IronForge.Shared.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace IronForge.Application.IntegrationTests.Auth;

/// <summary>
/// Sign-in, tenant selection and refresh against a real database.
/// Every call uses a fresh DbContext, like one HTTP request each.
/// </summary>
public sealed class AuthServiceTenantTests
    : IntegrationTestBase,
      IClassFixture<PostgreSqlTestDatabase>
{
    private const string Password = "Correct-Horse-1";

    public AuthServiceTenantTests(
        PostgreSqlTestDatabase database) : base(database)
    {
    }

    [Fact]
    public async Task Login_IssuesTokenWithoutTenantRoleOrPermissions()
    {
        // The account-wide role is Admin; it must not leak into any token.
        var seed = await SeedAsync(userRole: "Admin");

        var login = await LoginAsync(seed.Username);

        var token = Read(login.AccessToken);

        Assert.DoesNotContain(token.Claims, x => x.Type == CustomClaimTypes.TenantId);
        Assert.DoesNotContain(token.Claims, x => x.Type == ClaimTypes.Role);
        Assert.DoesNotContain(token.Claims, x => x.Type == CustomClaimTypes.Permission);

        // No leftover test identities in user tokens.
        Assert.DoesNotContain(token.Claims, x => x.Type == CustomClaimTypes.AgentId);
        Assert.DoesNotContain(token.Claims, x => x.Type == CustomClaimTypes.ClientId);
    }

    [Fact]
    public async Task PlatformAdmin_GetsPlatformPermissionWithAndWithoutTenant()
    {
        var seed = await SeedAsync(
            userRole: PlatformRoles.PlatformAdmin,
            roleInA: TenantRoles.Viewer);

        var login = await LoginAsync(seed.Username);

        // Without a tenant: only the platform permission, no tenant role.
        Assert.Equal(
            [Permissions.PlatformTenantsManage],
            PermissionsOf(login.AccessToken));
        Assert.DoesNotContain(Read(login.AccessToken).Claims, x => x.Type == ClaimTypes.Role);

        // With a tenant: the tenant role's permissions plus the platform one.
        var inA = await SelectTenantAsync(seed, seed.TenantA, login.RefreshToken);

        Assert.NotNull(inA);
        AssertTenantToken(inA!.AccessToken, seed.TenantA, TenantRoles.Viewer);
        Assert.Contains(Permissions.PlatformTenantsManage, PermissionsOf(inA.AccessToken));
        Assert.DoesNotContain(Permissions.ProductsCreate, PermissionsOf(inA.AccessToken));
    }

    [Fact]
    public async Task SelectTenant_UsesMembershipRoleOfSelectedTenant()
    {
        var seed = await SeedAsync(
            userRole: "Admin",
            roleInA: "Member",
            roleInB: "Admin");

        var login = await LoginAsync(seed.Username);

        var inA = await SelectTenantAsync(seed, seed.TenantA, login.RefreshToken);

        Assert.NotNull(inA);
        AssertTenantToken(inA!.AccessToken, seed.TenantA, "Member");
        Assert.DoesNotContain(PermissionsOf(inA.AccessToken), x => x == Permissions.AgentsCreate);
        Assert.Contains(PermissionsOf(inA.AccessToken), x => x == Permissions.ProductsRead);

        // Switching tenant with the rotated refresh token picks up that tenant's role.
        var inB = await SelectTenantAsync(seed, seed.TenantB, inA.RefreshToken);

        Assert.NotNull(inB);
        AssertTenantToken(inB!.AccessToken, seed.TenantB, "Admin");
        Assert.Contains(PermissionsOf(inB.AccessToken), x => x == Permissions.AgentsCreate);
    }

    [Fact]
    public async Task SelectTenant_WithInactiveMembership_IsRejected()
    {
        var seed = await SeedAsync();

        await UpdateMembershipAsync(seed.UserId, seed.TenantA, x => x.IsActive = false);

        var login = await LoginAsync(seed.Username);

        Assert.Null(await SelectTenantAsync(seed, seed.TenantA, login.RefreshToken));
    }

    [Fact]
    public async Task Refresh_WithActiveMembership_RereadsMembershipRole()
    {
        var seed = await SeedAsync(roleInA: "Member");

        var session = await LoginAndSelectAsync(seed, seed.TenantA);

        await UpdateMembershipAsync(seed.UserId, seed.TenantA, x => x.Role = "Admin");

        var refreshed = await RefreshAsync(session.RefreshToken);

        Assert.NotNull(refreshed);
        AssertTenantToken(refreshed!.AccessToken, seed.TenantA, "Admin");
        Assert.Contains(PermissionsOf(refreshed.AccessToken), x => x == Permissions.AgentsCreate);
    }

    [Fact]
    public async Task Refresh_AfterMembershipDeactivated_IsRejectedAndEndsSession()
    {
        var seed = await SeedAsync();

        var session = await LoginAndSelectAsync(seed, seed.TenantA);

        await UpdateMembershipAsync(seed.UserId, seed.TenantA, x => x.IsActive = false);

        Assert.Null(await RefreshAsync(session.RefreshToken));

        await AssertAllRefreshTokensRevokedAsync(seed.UserId);
    }

    [Fact]
    public async Task Refresh_AfterMembershipRemoved_IsRejectedAndEndsSession()
    {
        var seed = await SeedAsync();

        var session = await LoginAndSelectAsync(seed, seed.TenantA);

        await using (var db = CreateDbContext())
        {
            var membership = await db.TenantMemberships.SingleAsync(
                x => x.UserId == seed.UserId && x.TenantId == seed.TenantA);

            db.TenantMemberships.Remove(membership);

            await db.SaveChangesAsync();
        }

        Assert.Null(await RefreshAsync(session.RefreshToken));

        await AssertAllRefreshTokensRevokedAsync(seed.UserId);
    }

    [Fact]
    public async Task Refresh_AfterTenantDeactivated_IsRejectedAndEndsSession()
    {
        var seed = await SeedAsync();

        var session = await LoginAndSelectAsync(seed, seed.TenantA);

        await using (var db = CreateDbContext())
        {
            var tenant = await db.Tenants.SingleAsync(x => x.Id == seed.TenantA);

            tenant.IsActive = false;

            await db.SaveChangesAsync();
        }

        Assert.Null(await RefreshAsync(session.RefreshToken));

        await AssertAllRefreshTokensRevokedAsync(seed.UserId);
    }

    [Fact]
    public async Task Refresh_WithoutTenant_StillWorks()
    {
        var seed = await SeedAsync();

        var login = await LoginAsync(seed.Username);

        var refreshed = await RefreshAsync(login.RefreshToken);

        Assert.NotNull(refreshed);
        Assert.DoesNotContain(
            Read(refreshed!.AccessToken).Claims,
            x => x.Type == CustomClaimTypes.TenantId);
    }

    private sealed record Seed(
        int UserId,
        string Username,
        int TenantA,
        int TenantB);

    private IronForgeDbContext CreateDbContext()
    {
        // Authentication runs before a tenant is selected.
        return Database.CreateDbContext(
            new IronForge.Application.Execution.ExecutionContext
            {
                Source = ExecutionSource.Web
            });
    }

    private static AuthService CreateAuthService(IronForgeDbContext db)
    {
        return new TestServiceFactory(db, TestServiceFactory.Context()).Auth;
    }

    private async Task<Seed> SeedAsync(
        string userRole = "User",
        string roleInA = "Member",
        string roleInB = "Member")
    {
        await using var db = CreateDbContext();

        var now = DateTime.UtcNow;

        var tenantA = new Tenant { Name = "Tenant A", Slug = "tenant-a", IsActive = true, CreatedAtUtc = now };
        var tenantB = new Tenant { Name = "Tenant B", Slug = "tenant-b", IsActive = true, CreatedAtUtc = now };

        var user = new User
        {
            Username = "tenant-user",
            Email = "tenant-user@example.com",
            Role = userRole,
            IsActive = true,
            CreatedAtUtc = now
        };

        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Password);

        db.Tenants.AddRange(tenantA, tenantB);
        db.Users.Add(user);

        await db.SaveChangesAsync();

        db.TenantMemberships.AddRange(
            new TenantMembership { TenantId = tenantA.Id, UserId = user.Id, Role = roleInA, IsActive = true, CreatedAtUtc = now },
            new TenantMembership { TenantId = tenantB.Id, UserId = user.Id, Role = roleInB, IsActive = true, CreatedAtUtc = now });

        await db.SaveChangesAsync();

        return new Seed(user.Id, user.Username, tenantA.Id, tenantB.Id);
    }

    private async Task<LoginResponse> LoginAsync(string username)
    {
        await using var db = CreateDbContext();

        var response = await CreateAuthService(db).LoginAsync(
            new LoginRequest { Username = username, Password = Password });

        Assert.NotNull(response);

        return response!;
    }

    private async Task<LoginResponse?> SelectTenantAsync(
        Seed seed,
        int tenantId,
        string refreshToken)
    {
        await using var db = CreateDbContext();

        return await CreateAuthService(db).SelectTenantAsync(
            seed.UserId,
            tenantId,
            refreshToken);
    }

    private async Task<LoginResponse> LoginAndSelectAsync(Seed seed, int tenantId)
    {
        var login = await LoginAsync(seed.Username);

        var selected = await SelectTenantAsync(seed, tenantId, login.RefreshToken);

        Assert.NotNull(selected);

        return selected!;
    }

    private async Task<LoginResponse?> RefreshAsync(string refreshToken)
    {
        await using var db = CreateDbContext();

        return await CreateAuthService(db).RefreshTokenAsync(refreshToken);
    }

    private async Task UpdateMembershipAsync(
        int userId,
        int tenantId,
        Action<TenantMembership> update)
    {
        await using var db = CreateDbContext();

        var membership = await db.TenantMemberships.SingleAsync(
            x => x.UserId == userId && x.TenantId == tenantId);

        update(membership);

        await db.SaveChangesAsync();
    }

    private async Task AssertAllRefreshTokensRevokedAsync(int userId)
    {
        await using var db = CreateDbContext();

        var tokens = await db.RefreshTokens
            .Where(x => x.UserId == userId)
            .ToListAsync();

        Assert.NotEmpty(tokens);
        Assert.All(tokens, x => Assert.NotNull(x.RevokedAtUtc));
    }

    private static JwtSecurityToken Read(string accessToken)
    {
        return new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
    }

    private static List<string> PermissionsOf(string accessToken)
    {
        return Read(accessToken).Claims
            .Where(x => x.Type == CustomClaimTypes.Permission)
            .Select(x => x.Value)
            .ToList();
    }

    private static void AssertTenantToken(
        string accessToken,
        int tenantId,
        string role)
    {
        var token = Read(accessToken);

        Assert.Equal(
            tenantId.ToString(),
            token.Claims.Single(x => x.Type == CustomClaimTypes.TenantId).Value);

        Assert.Equal(
            role,
            token.Claims.Single(x => x.Type == ClaimTypes.Role).Value);
    }
}
