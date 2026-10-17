using IronForge.Application.Configurations;
using IronForge.Application.Entities;
using IronForge.Application.IntegrationTests.Infrastructure;
using IronForge.Application.Auth.Services;
using IronForge.Infrastructure.Data.Seeding;
using IronForge.Infrastructure.Persistence;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Tenants;
using IronForge.Shared.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IronForge.Application.IntegrationTests.Gym;

/// <summary>The platform operator's cross-tenant views, over the demo data.</summary>
public sealed class PlatformInsightsTests
    : IntegrationTestBase,
      IClassFixture<PostgreSqlTestDatabase>
{
    public PlatformInsightsTests(PostgreSqlTestDatabase database) : base(database)
    {
    }

    [Fact]
    public async Task OperatorViews_CoverEveryTenant()
    {
        await SeedAsync();

        var (overview, catalog, audit, detail) = await AsOperator(async s =>
        {
            var o = await s.GetOverviewAsync();
            var c = await s.GetCatalogAsync();
            var a = await s.GetAuditAsync(new PlatformAuditQuery { Days = 30, Take = 50 });

            var titanId = o.Data!.PastDue.Single().TenantId;
            var d = await s.GetTenantAsync(titanId);

            return (o, c, a, d);
        });

        Assert.True(overview.Success, overview.ErrorMessage);
        Assert.Equal(4, overview.Data!.TenantCount);
        Assert.Equal(1, overview.Data.TrialTenants);
        Assert.Equal(1, overview.Data.CanceledTenants);
        Assert.Single(overview.Data.TrialsEndingSoon);
        Assert.True(overview.Data.ActiveGymMembers > 30);
        Assert.True(overview.Data.TokensThisMonth > 0);
        Assert.Equal(30, overview.Data.TokensByDay.Count);

        Assert.True(catalog.Success, catalog.ErrorMessage);
        Assert.Equal(SubscriptionPlans.All.Count, catalog.Data!.Plans.Count);
        Assert.Contains(catalog.Data.AgentTemplates, x => x.TemplateId == "fitness-coach" && x.InstalledTenants == 2);

        Assert.True(audit.Success, audit.ErrorMessage);
        Assert.NotEmpty(audit.Data!);
        Assert.All(audit.Data!, x => Assert.NotNull(x.TenantName));

        Assert.True(detail.Success, detail.ErrorMessage);
        Assert.Equal("Titan Strength Club", detail.Data!.Tenant.Name);
        Assert.Equal("GBP", detail.Data.Currency);
        Assert.True(detail.Data.MembersByStatus["Active"] > 0);
        Assert.True(detail.Data.GymRevenueLast30Days > 0);
        Assert.Equal(3, detail.Data.Agents.Count);
    }

    [Fact]
    public async Task TenantStaff_CannotUseOperatorViews()
    {
        await SeedAsync();

        var context = TestServiceFactory.AsRole(1, 1, TenantRoles.Admin);

        await using var db = Database.CreateDbContext(context);
        var services = new TestServiceFactory(db, context);

        var result = await Create(services).GetOverviewAsync();

        Assert.Equal(403, result.StatusCode);
    }

    private async Task SeedAsync()
    {
        await using var db = Database.CreateDbContext();
        await new DemoDataSeeder(db, new PasswordHasher<User>(), NullLogger.Instance).SeedAsync();
    }

    private async Task<T> AsOperator<T>(Func<PlatformInsightsService, Task<T>> action)
    {
        var context = TestServiceFactory.Context(null, 1, Permissions.PlatformTenantsManage);

        await using var db = Database.CreateDbContext(context);
        var services = new TestServiceFactory(db, context);

        return await action(Create(services));
    }

    private static PlatformInsightsService Create(TestServiceFactory s) =>
        new(
            s.Db,
            new UserAuthorizationService(s.ContextAccessor),
            new TestModelProfiles(),
            Options.Create(new AgentQuotaOptions()),
            TimeProvider.System);
}
