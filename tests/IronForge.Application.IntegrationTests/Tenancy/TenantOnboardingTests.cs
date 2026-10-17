using IronForge.Application.Agents.Runtime;
using IronForge.Application.Auth.DTOs;
using IronForge.Application.Auth.Models;
using IronForge.Application.Entities;
using IronForge.Application.IntegrationTests.Infrastructure;
using IronForge.Application.Tenants.Invitations;
using IronForge.Infrastructure.Data;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Tenants;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace IronForge.Application.IntegrationTests.Tenancy;

/// <summary>
/// Client onboarding end to end: the platform operator provisions a tenant,
/// the invited Admin accepts, signs in, and manages members.
/// Every service call uses a fresh DbContext, like one HTTP request.
/// </summary>
public sealed class TenantOnboardingTests
    : IntegrationTestBase,
      IClassFixture<PostgreSqlTestDatabase>
{
    private const string Password = "Correct-Horse-1";

    public TenantOnboardingTests(
        PostgreSqlTestDatabase database) : base(database)
    {
    }

    // ---- Provisioning --------------------------------------------------

    [Fact]
    public async Task Provision_CreatesTenantTemplateAgentsAndAdminInvitation()
    {
        var result = await AsPlatformAdmin(s => s.Provisioning.ProvisionAsync(
            NewTenant("acme", "IT@Acme.com")));

        Assert.True(result.Success, result.ErrorMessage);

        var tenantId = result.Data!.Tenant.TenantId;
        var link = result.Data.AdminInvitation;

        await using var db = Database.CreateDbContext();

        var tenant = await db.Tenants.SingleAsync(x => x.Id == tenantId);
        Assert.Equal("acme", tenant.Slug);
        Assert.True(tenant.IsActive);

        // Each template is installed with its tool bindings (which define its permissions).
        var agents = await db.Agents
            .IgnoreQueryFilters()
            .Include(x => x.ToolBindings)
            .Where(x => x.TenantId == tenantId)
            .ToListAsync();

        Assert.Equal(AgentTemplates.Default.Count, agents.Count);

        foreach (var template in AgentTemplates.Default)
        {
            var agent = agents.Single(x => x.AgentId == template.Definition.AgentId);

            Assert.Equal(
                template.Definition.Tools.Select(x => x.ToolName).Order(),
                agent.ToolBindings.Select(x => x.ToolName).Order());
        }

        // The invitation is for an Admin; only the token's hash is stored.
        var invitation = await db.TenantInvitations
            .IgnoreQueryFilters()
            .SingleAsync(x => x.TenantId == tenantId);

        Assert.Equal("it@acme.com", invitation.Email);
        Assert.Equal(TenantRoles.Admin, invitation.Role);
        Assert.Equal(InvitationToken.Hash(link.Token), invitation.TokenHash);
        Assert.NotEqual(link.Token, invitation.TokenHash);
        Assert.StartsWith(TestServiceFactory.AcceptUrl + "?token=", link.AcceptUrl);
    }

    [Fact]
    public async Task Provision_WithoutPlatformPermission_IsForbidden()
    {
        // A tenant Admin is not a platform operator.
        var result = await AsUser(
            tenantId: null,
            userId: null,
            [Permissions.MembersManage],
            s => s.Provisioning.ProvisionAsync(NewTenant("acme", "it@acme.com")));

        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);

        await using var db = Database.CreateDbContext();
        Assert.False(await db.Tenants.AnyAsync());
    }

    [Fact]
    public async Task Provision_WithExistingSlug_IsRejected()
    {
        await ProvisionAsync("acme", "it@acme.com");

        var result = await AsPlatformAdmin(s => s.Provisioning.ProvisionAsync(
            NewTenant("acme", "other@acme.com")));

        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Provision_WhenAdminInvitationFails_RollsBackEverything()
    {
        // The email is validated by the API model binder; the service rejects
        // it inside the transaction, after tenant and agents were saved.
        var result = await AsPlatformAdmin(s => s.Provisioning.ProvisionAsync(
            NewTenant("acme", adminEmail: " ")));

        Assert.False(result.Success);
        Assert.Equal(400, result.StatusCode);

        await using var db = Database.CreateDbContext();
        Assert.False(await db.Tenants.AnyAsync());
        Assert.False(await db.Agents.IgnoreQueryFilters().AnyAsync());
        Assert.False(await db.TenantInvitations.IgnoreQueryFilters().AnyAsync());
    }

    // ---- Accepting invitations -----------------------------------------

    [Fact]
    public async Task Accept_NewAccount_JoinsAsAdminAndCanSignIn()
    {
        var provisioned = await ProvisionAsync("acme", "it@acme.com");

        var preview = await Anonymous(s => s.Invitations.PreviewAsync(provisioned.Token));

        Assert.True(preview.Success, preview.ErrorMessage);
        Assert.Equal("Acme", preview.Data!.TenantName);
        Assert.False(preview.Data.AccountExists);

        var accepted = await AcceptAsync(provisioned.Token, "acme-admin");

        Assert.True(accepted.Success, accepted.ErrorMessage);
        Assert.True(accepted.Data!.AccountCreated);
        Assert.Equal(TenantRoles.Admin, accepted.Data.Role);

        // The new account is a regular platform user with an Admin membership.
        await using (var db = Database.CreateDbContext())
        {
            var user = await db.Users.SingleAsync(x => x.Username == "acme-admin");
            Assert.Equal("it@acme.com", user.Email);
            Assert.Equal(PlatformRoles.User, user.Role);

            var membership = await db.TenantMemberships.SingleAsync(x => x.UserId == user.Id);
            Assert.Equal(provisioned.TenantId, membership.TenantId);
            Assert.Equal(TenantRoles.Admin, membership.Role);
            Assert.True(membership.IsActive);
        }

        // Signing in and selecting the tenant gives the Admin permissions.
        var accessToken = await SignInToTenantAsync("acme-admin", provisioned.TenantId);

        Assert.Contains(Permissions.MembersManage, PermissionsOf(accessToken));
        Assert.DoesNotContain(Permissions.PlatformTenantsManage, PermissionsOf(accessToken));
    }

    [Fact]
    public async Task Accept_IsSingleUse()
    {
        var provisioned = await ProvisionAsync("acme", "it@acme.com");

        Assert.True((await AcceptAsync(provisioned.Token, "acme-admin")).Success);

        var second = await AcceptAsync(provisioned.Token, "someone-else");

        Assert.False(second.Success);
        Assert.Equal("This invitation has already been used.", second.ErrorMessage);

        await using var db = Database.CreateDbContext();
        Assert.False(await db.Users.AnyAsync(x => x.Username == "someone-else"));
    }

    [Fact]
    public async Task Accept_ExistingAccount_RequiresThatAccountsPassword()
    {
        var provisioned = await ProvisionAsync("acme", "it@acme.com");
        var userId = await SeedUserAsync("existing", "IT@acme.com");

        var preview = await Anonymous(s => s.Invitations.PreviewAsync(provisioned.Token));
        Assert.True(preview.Data!.AccountExists);

        var wrongPassword = await AcceptAsync(provisioned.Token, "existing", "Wrong-Password-1");

        Assert.Equal(401, wrongPassword.StatusCode);

        // Someone else's username cannot be used to claim the invitation either.
        await SeedUserAsync("intruder", "intruder@example.com");
        var otherAccount = await AcceptAsync(provisioned.Token, "intruder");

        Assert.Equal(401, otherAccount.StatusCode);

        await using (var db = Database.CreateDbContext())
        {
            Assert.False(await db.TenantMemberships.AnyAsync(x => x.TenantId == provisioned.TenantId));
        }

        var accepted = await AcceptAsync(provisioned.Token, "existing");

        Assert.True(accepted.Success, accepted.ErrorMessage);
        Assert.False(accepted.Data!.AccountCreated);

        await using (var db = Database.CreateDbContext())
        {
            var membership = await db.TenantMemberships.SingleAsync(
                x => x.TenantId == provisioned.TenantId);

            Assert.Equal(userId, membership.UserId);
        }
    }

    [Fact]
    public async Task Accept_ExpiredInvitation_IsRejected()
    {
        var provisioned = await ProvisionAsync("acme", "it@acme.com");

        await using (var db = Database.CreateDbContext())
        {
            var invitation = await db.TenantInvitations.IgnoreQueryFilters().SingleAsync();
            invitation.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var result = await AcceptAsync(provisioned.Token, "acme-admin");

        Assert.False(result.Success);
        Assert.Contains("expired", result.ErrorMessage);
    }

    [Fact]
    public async Task Accept_WhenTenantSuspended_IsRejected()
    {
        var provisioned = await ProvisionAsync("acme", "it@acme.com");

        await AsPlatformAdmin(s => s.Provisioning.SetStatusAsync(provisioned.TenantId, isActive: false));

        var result = await AcceptAsync(provisioned.Token, "acme-admin");

        Assert.False(result.Success);
        Assert.Equal("This workspace is not available.", result.ErrorMessage);
    }

    [Fact]
    public async Task NewInvitation_ReplacesOpenInvitationForSameEmail()
    {
        var provisioned = await ProvisionAsync("acme", "it@acme.com");

        var replacement = await AsPlatformAdmin(s =>
            s.Provisioning.InviteAdminAsync(provisioned.TenantId, "it@acme.com"));

        Assert.True(replacement.Success, replacement.ErrorMessage);

        var oldLink = await AcceptAsync(provisioned.Token, "acme-admin");
        Assert.Contains("withdrawn", oldLink.ErrorMessage);

        var newLink = await AcceptAsync(replacement.Data!.Token, "acme-admin");
        Assert.True(newLink.Success, newLink.ErrorMessage);
    }

    // ---- Tenant admin: members and invitations -------------------------

    [Fact]
    public async Task Admin_InvitesMemberWithRole_AndRevokedInvitationCannotBeUsed()
    {
        var (tenantId, adminId) = await OnboardAsync("acme");

        var invite = await AsTenantAdmin(tenantId, adminId, s =>
            s.Invitations.CreateForCurrentTenantAsync(new CreateTenantInvitationRequest
            {
                Email = "trainer@acme.com",
                Role = "trainer"
            }));

        Assert.True(invite.Success, invite.ErrorMessage);
        Assert.Equal(TenantRoles.Trainer, invite.Data!.Invitation.Role);

        var open = await AsTenantAdmin(tenantId, adminId, s => s.Invitations.ListForCurrentTenantAsync());
        Assert.Contains(open.Data!, x => x.Email == "trainer@acme.com");

        var revoked = await AsTenantAdmin(tenantId, adminId, s =>
            s.Invitations.RevokeForCurrentTenantAsync(invite.Data.Invitation.InvitationId));
        Assert.True(revoked.Success, revoked.ErrorMessage);

        var accept = await AcceptAsync(invite.Data.Token, "acme-trainer");
        Assert.False(accept.Success);
    }

    [Fact]
    public async Task Invite_ForActiveMember_IsRejected()
    {
        var (tenantId, adminId) = await OnboardAsync("acme");

        var result = await AsTenantAdmin(tenantId, adminId, s =>
            s.Invitations.CreateForCurrentTenantAsync(new CreateTenantInvitationRequest
            {
                Email = "admin@acme.com",
                Role = TenantRoles.Member
            }));

        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Invite_WithUnknownRole_IsRejected()
    {
        var (tenantId, adminId) = await OnboardAsync("acme");

        var result = await AsTenantAdmin(tenantId, adminId, s =>
            s.Invitations.CreateForCurrentTenantAsync(new CreateTenantInvitationRequest
            {
                Email = "x@acme.com",
                Role = "Owner"
            }));

        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Admin_ChangesMemberRole_AndNewRoleAppliesAtNextSignIn()
    {
        var (tenantId, adminId) = await OnboardAsync("acme");
        var memberId = await AddMemberAsync(tenantId, adminId, "member@acme.com", "acme-member");

        var before = await SignInToTenantAsync("acme-member", tenantId);
        Assert.DoesNotContain(Permissions.GymBillingManage, PermissionsOf(before));

        var changed = await AsTenantAdmin(tenantId, adminId, s =>
            s.Members.UpdateRoleAsync(memberId, TenantRoles.Admin));

        Assert.True(changed.Success, changed.ErrorMessage);
        Assert.Equal(TenantRoles.Admin, changed.Data!.Role);

        var token = await SignInToTenantAsync("acme-member", tenantId);

        Assert.Equal(
            TenantRoles.Admin,
            new JwtSecurityTokenHandler().ReadJwtToken(token).Claims
                .Single(x => x.Type == ClaimTypes.Role).Value);
        Assert.Contains(Permissions.GymBillingManage, PermissionsOf(token));
    }

    [Fact]
    public async Task Admin_CannotChangeOwnMembership()
    {
        var (tenantId, adminId) = await OnboardAsync("acme");

        var demote = await AsTenantAdmin(tenantId, adminId, s =>
            s.Members.UpdateRoleAsync(adminId, TenantRoles.Member));
        var deactivate = await AsTenantAdmin(tenantId, adminId, s =>
            s.Members.UpdateStatusAsync(adminId, isActive: false));
        var remove = await AsTenantAdmin(tenantId, adminId, s =>
            s.Members.RemoveAsync(adminId));

        Assert.Equal(400, demote.StatusCode);
        Assert.Equal(400, deactivate.StatusCode);
        Assert.Equal(400, remove.StatusCode);
    }

    [Fact]
    public async Task LastActiveAdmin_CannotBeDemotedDeactivatedOrRemoved()
    {
        var (tenantId, adminId) = await OnboardAsync("acme");

        // An actor holding the permission without being an Admin member
        // (e.g. support tooling) still cannot leave the tenant without an Admin.
        var actorId = await SeedUserAsync("support", "support@platform.test");

        var demote = await AsUser(tenantId, actorId, AdminPermissions(), s =>
            s.Members.UpdateRoleAsync(adminId, TenantRoles.Member));
        var deactivate = await AsUser(tenantId, actorId, AdminPermissions(), s =>
            s.Members.UpdateStatusAsync(adminId, isActive: false));
        var remove = await AsUser(tenantId, actorId, AdminPermissions(), s =>
            s.Members.RemoveAsync(adminId));

        Assert.Equal("The tenant must keep at least one active Admin.", demote.ErrorMessage);
        Assert.Equal("The tenant must keep at least one active Admin.", deactivate.ErrorMessage);
        Assert.Equal("The tenant must keep at least one active Admin.", remove.ErrorMessage);
    }

    [Fact]
    public async Task Admin_CannotSeeOrChangeMembersOfAnotherTenant()
    {
        var (acmeId, acmeAdminId) = await OnboardAsync("acme");
        var (globexId, globexAdminId) = await OnboardAsync("globex");
        var globexMemberId = await AddMemberAsync(globexId, globexAdminId, "member@globex.com", "globex-member");

        var list = await AsTenantAdmin(acmeId, acmeAdminId, s => s.Members.ListAsync());
        Assert.Equal([acmeAdminId], list.Data!.Select(x => x.UserId));

        var change = await AsTenantAdmin(acmeId, acmeAdminId, s =>
            s.Members.UpdateRoleAsync(globexMemberId, TenantRoles.Admin));
        var remove = await AsTenantAdmin(acmeId, acmeAdminId, s =>
            s.Members.RemoveAsync(globexMemberId));

        Assert.Equal(404, change.StatusCode);
        Assert.Equal(404, remove.StatusCode);

        await using var db = Database.CreateDbContext();
        var membership = await db.TenantMemberships.SingleAsync(
            x => x.TenantId == globexId && x.UserId == globexMemberId);
        Assert.Equal(TenantRoles.Trainer, membership.Role);
    }

    [Fact]
    public async Task Deactivating_Member_RevokesTheirDelegationsInThatTenantOnly_AndBlocksSignIn()
    {
        var (acmeId, acmeAdminId) = await OnboardAsync("acme");
        var (globexId, globexAdminId) = await OnboardAsync("globex");

        // One person who is a member of both tenants.
        var userId = await AddMemberAsync(acmeId, acmeAdminId, "shared@example.com", "shared-user");
        await AddMemberAsync(globexId, globexAdminId, "shared@example.com", "shared-user");

        var acmeDelegation = await SeedDelegationAsync(acmeId, userId);
        var globexDelegation = await SeedDelegationAsync(globexId, userId);

        var result = await AsTenantAdmin(acmeId, acmeAdminId, s =>
            s.Members.UpdateStatusAsync(userId, isActive: false));

        Assert.True(result.Success, result.ErrorMessage);

        await using (var db = Database.CreateDbContext())
        {
            var delegations = await db.AgentDelegations.IgnoreQueryFilters().ToListAsync();

            Assert.True(delegations.Single(x => x.DelegationId == acmeDelegation).Revoked);
            Assert.False(delegations.Single(x => x.DelegationId == globexDelegation).Revoked);
        }

        // Selecting the deactivated tenant fails; the other tenant still works.
        var login = await LoginAsync("shared-user");

        await using (var db = Database.CreateDbContext())
        {
            Assert.Null(await Services(db).Auth.SelectTenantAsync(userId, acmeId, login.RefreshToken));
        }

        Assert.NotNull(await SignInToTenantAsync("shared-user", globexId));
    }

    [Fact]
    public async Task Trainer_CannotManageStaff()
    {
        var (tenantId, adminId) = await OnboardAsync("acme");
        var trainerId = await AddMemberAsync(tenantId, adminId, "trainer@acme.com", "acme-trainer", TenantRoles.Trainer);

        var trainerPermissions = RolePermissions.GetPermissions(TenantRoles.Trainer);

        var list = await AsUser(tenantId, trainerId, trainerPermissions, s => s.Members.ListAsync());
        var change = await AsUser(tenantId, trainerId, trainerPermissions, s =>
            s.Members.UpdateRoleAsync(adminId, TenantRoles.Trainer));

        Assert.Equal(403, list.StatusCode);
        Assert.Equal(403, change.StatusCode);
    }

    // ---- Helpers --------------------------------------------------------

    private sealed record Provisioned(int TenantId, string Token);

    private static ProvisionTenantRequest NewTenant(string slug, string adminEmail) =>
        new()
        {
            Name = char.ToUpperInvariant(slug[0]) + slug[1..],
            Slug = slug,
            AdminEmail = adminEmail
        };

    private static string[] AdminPermissions() =>
        RolePermissions.GetPermissions(TenantRoles.Admin);

    private TestServiceFactory Services(
        IronForgeDbContext db,
        IronForge.Application.Execution.ExecutionContext? context = null)
    {
        return new TestServiceFactory(db, context ?? TestServiceFactory.Context());
    }

    private async Task<T> AsUser<T>(
        int? tenantId,
        int? userId,
        string[] permissions,
        Func<TestServiceFactory, Task<T>> action)
    {
        await using var db = Database.CreateDbContext(
            TestServiceFactory.Context(tenantId, userId, permissions));

        return await action(Services(
            db,
            TestServiceFactory.Context(tenantId, userId, permissions)));
    }

    private Task<T> AsPlatformAdmin<T>(Func<TestServiceFactory, Task<T>> action) =>
        AsUser(null, null, [Permissions.PlatformTenantsManage], action);

    private Task<T> AsTenantAdmin<T>(int tenantId, int adminId, Func<TestServiceFactory, Task<T>> action) =>
        AsUser(tenantId, adminId, AdminPermissions(), action);

    private Task<T> Anonymous<T>(Func<TestServiceFactory, Task<T>> action) =>
        AsUser(null, null, [], action);

    private async Task<Provisioned> ProvisionAsync(string slug, string adminEmail)
    {
        var result = await AsPlatformAdmin(s => s.Provisioning.ProvisionAsync(
            NewTenant(slug, adminEmail)));

        Assert.True(result.Success, result.ErrorMessage);

        return new Provisioned(
            result.Data!.Tenant.TenantId,
            result.Data.AdminInvitation.Token);
    }

    /// <summary>Provisions a tenant and accepts its Admin invitation.</summary>
    private async Task<(int TenantId, int AdminId)> OnboardAsync(string slug)
    {
        var provisioned = await ProvisionAsync(slug, $"admin@{slug}.com");

        var accepted = await AcceptAsync(provisioned.Token, $"{slug}-admin");
        Assert.True(accepted.Success, accepted.ErrorMessage);

        await using var db = Database.CreateDbContext();
        var adminId = await db.Users
            .Where(x => x.Username == $"{slug}-admin")
            .Select(x => x.Id)
            .SingleAsync();

        return (provisioned.TenantId, adminId);
    }

    /// <summary>Invites and accepts; reuses the account if the email already has one.</summary>
    private async Task<int> AddMemberAsync(
        int tenantId,
        int adminId,
        string email,
        string username,
        // Staff accounts: gym members invited without a staff-created profile
        // wait for approval (see GymWorkflowTests).
        string role = TenantRoles.Trainer)
    {
        var invite = await AsTenantAdmin(tenantId, adminId, s =>
            s.Invitations.CreateForCurrentTenantAsync(new CreateTenantInvitationRequest
            {
                Email = email,
                Role = role
            }));

        Assert.True(invite.Success, invite.ErrorMessage);

        var accepted = await AcceptAsync(invite.Data!.Token, username);
        Assert.True(accepted.Success, accepted.ErrorMessage);

        await using var db = Database.CreateDbContext();
        return await db.Users
            .Where(x => x.Username == username)
            .Select(x => x.Id)
            .SingleAsync();
    }

    private Task<IronForge.Application.Commons.ServiceResult<AcceptInvitationResultDto>> AcceptAsync(
        string token,
        string username,
        string password = Password)
    {
        return Anonymous(s => s.Invitations.AcceptAsync(new AcceptInvitationRequest
        {
            Token = token,
            Username = username,
            Password = password
        }));
    }

    private async Task<int> SeedUserAsync(string username, string email)
    {
        await using var db = Database.CreateDbContext();

        var user = new User
        {
            Username = username,
            Email = email,
            Role = PlatformRoles.User,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Password);

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return user.Id;
    }

    private async Task<Guid> SeedDelegationAsync(int tenantId, int userId)
    {
        await using var db = Database.CreateDbContext();

        var delegation = new AgentDelegationEntity
        {
            DelegationId = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            AgentId = AgentTemplates.GymAdmin.Definition.AgentId,
            ClientId = "mcp-client",
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30)
        };

        db.AgentDelegations.Add(delegation);
        await db.SaveChangesAsync();

        return delegation.DelegationId;
    }

    private async Task<LoginResponse> LoginAsync(string username)
    {
        await using var db = Database.CreateDbContext();

        var login = await Services(db).Auth.LoginAsync(
            new LoginRequest { Username = username, Password = Password });

        Assert.NotNull(login);

        return login!;
    }

    private async Task<string> SignInToTenantAsync(string username, int tenantId)
    {
        var login = await LoginAsync(username);

        await using var db = Database.CreateDbContext();

        var userId = await db.Users
            .Where(x => x.Username == username)
            .Select(x => x.Id)
            .SingleAsync();

        var selected = await Services(db).Auth.SelectTenantAsync(
            userId,
            tenantId,
            login.RefreshToken);

        Assert.NotNull(selected);

        return selected!.AccessToken;
    }

    private static List<string> PermissionsOf(string accessToken)
    {
        return new JwtSecurityTokenHandler()
            .ReadJwtToken(accessToken)
            .Claims
            .Where(x => x.Type == CustomClaimTypes.Permission)
            .Select(x => x.Value)
            .ToList();
    }
}
