using IronForge.Application.Agents.Approvals;
using IronForge.Application.Agents.Authorization;
using IronForge.Application.Agents.Governance;
using IronForge.Application.Auditing.Models;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.IntegrationTests.Infrastructure;
using IronForge.Infrastructure.Data;
using IronForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IronForge.Application.IntegrationTests.Tenancy;

/// <summary>
/// Seeds the same data shape into two tenants and verifies that every
/// tenant-owned table only returns the current tenant's rows, and nothing
/// when no tenant is selected. Queries deliberately add no tenant condition:
/// isolation must come from the DbContext query filters alone.
/// </summary>
public sealed class TenantQueryFilterTests
    : IntegrationTestBase,
      IClassFixture<PostgreSqlTestDatabase>
{
    public TenantQueryFilterTests(
        PostgreSqlTestDatabase database) : base(database)
    {
    }

    [Fact]
    public async Task TenantOwnedTables_OnlyReturnCurrentTenantRows()
    {
        var seed = await SeedTwoTenantsAsync();

        foreach (var (tenantId, other) in new[]
                 {
                     (seed.TenantA, seed.TenantB),
                     (seed.TenantB, seed.TenantA)
                 })
        {
            await using var db = CreateDbContext(tenantId);

            var approvals = await db.AgentApprovals.ToListAsync();
            Assert.Single(approvals);
            Assert.All(approvals, x => Assert.Equal(tenantId, x.TenantId));

            var auditEvents = await db.AuditEvents.ToListAsync();
            Assert.Single(auditEvents);
            Assert.All(auditEvents, x => Assert.Equal(tenantId, x.TenantId));

            // Direct lookups of the other tenant's rows by key find nothing.
            Assert.False(await db.AgentApprovals
                .AnyAsync(x => x.ApprovalId == seed.ApprovalIdByTenant[other]));
        }
    }

    /// <summary>
    /// Without a tenant the filters cannot be evaluated (they read
    /// CurrentTenantId.Value), so the query fails instead of returning rows.
    /// This matches every other tenant-owned table in the DbContext.
    /// </summary>
    [Fact]
    public async Task TenantOwnedTables_CannotBeQueriedWithoutTenant()
    {
        await SeedTwoTenantsAsync();

        await using var db = CreateDbContext(tenantId: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.AgentApprovals.ToListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task AuditEventsWithoutTenant_AreNotVisibleToAnyTenant()
    {
        var seed = await SeedTwoTenantsAsync();

        await using var db = CreateDbContext(seed.TenantA);

        Assert.False(await db.AuditEvents
            .AnyAsync(x => x.Action == PlatformAuditAction));

        // The row exists; it is only hidden by the filter.
        Assert.True(await db.AuditEvents
            .IgnoreQueryFilters()
            .AnyAsync(x => x.Action == PlatformAuditAction));
    }

    [Fact]
    public async Task ApprovalStore_CannotReadOtherTenantsApprovals()
    {
        var seed = await SeedTwoTenantsAsync();

        await using var db = CreateDbContext(seed.TenantA);

        var store = new DBAgentApprovalStore(
            new EfRepository<AgentApproval>(db));

        Assert.NotNull(await store.GetAsync(
            seed.ApprovalIdByTenant[seed.TenantA]));

        Assert.Null(await store.GetAsync(
            seed.ApprovalIdByTenant[seed.TenantB]));
    }

    private const string SharedAgentId = "shared-agent";
    private const string PlatformAuditAction = "PlatformEvent";

    private sealed record SeedResult(
        int TenantA,
        int TenantB,
        int UserId,
        Dictionary<int, int> AgentIdByTenant,
        Dictionary<int, string> ApprovalIdByTenant);

    private IronForgeDbContext CreateDbContext(int? tenantId)
    {
        return Database.CreateDbContext(
            new IronForge.Application.Execution.ExecutionContext
            {
                TenantId = tenantId,
                Source = ExecutionSource.Web
            });
    }

    /// <summary>
    /// Inserts are not affected by query filters, so seeding runs without
    /// a tenant. Each tenant gets one row in every tenant-owned table.
    /// </summary>
    private async Task<SeedResult> SeedTwoTenantsAsync()
    {
        await using var db = CreateDbContext(tenantId: null);

        var now = DateTime.UtcNow;

        var tenantA = new Tenant { Name = "Tenant A", Slug = "tenant-a", IsActive = true, CreatedAtUtc = now };
        var tenantB = new Tenant { Name = "Tenant B", Slug = "tenant-b", IsActive = true, CreatedAtUtc = now };

        var user = new User
        {
            Username = "shared-user",
            Email = "shared-user@example.com",
            PasswordHash = "not-used",
            IsActive = true,
            CreatedAtUtc = now
        };

        db.Tenants.AddRange(tenantA, tenantB);
        db.Users.Add(user);

        await db.SaveChangesAsync();

        var agentIdByTenant = new Dictionary<int, int>();
        var approvalIdByTenant = new Dictionary<int, string>();

        foreach (var tenant in new[] { tenantA, tenantB })
        {
            var agent = new Agent
            {
                TenantId = tenant.Id,
                AgentId = SharedAgentId,
                Name = $"Agent {tenant.Slug}",
                Version = "1.0",
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            db.Agents.Add(agent);

            await db.SaveChangesAsync();

            agentIdByTenant[tenant.Id] = agent.Id;

            var approvalId = $"approval-{tenant.Slug}";
            approvalIdByTenant[tenant.Id] = approvalId;

            db.AgentApprovals.Add(new AgentApproval
            {
                ApprovalId = approvalId,
                RequestId = $"request-{tenant.Slug}",
                SessionId = Guid.NewGuid(),
                TenantId = tenant.Id,
                UserId = user.Id,
                AgentId = SharedAgentId,
                ToolName = "send_payment_reminder",
                ToolCallId = $"call-{tenant.Slug}",
                ArgumentsJson = "{}",
                RiskLevel = AgentRiskLevel.Medium,
                Status = AgentApprovalStatus.Pending,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddMinutes(10)
            });

            db.AuditEvents.Add(new AuditEvent
            {
                TenantId = tenant.Id,
                OccurredAtUtc = now,
                Category = AuditCategory.Billing,
                Action = $"Event-{tenant.Slug}",
                Outcome = AuditOutcome.Succeeded,
                ActorType = AuditActorType.User,
                UserId = user.Id
            });

        }

        // A platform-level event (for example a failed sign-in) has no tenant.
        db.AuditEvents.Add(new AuditEvent
        {
            TenantId = null,
            OccurredAtUtc = now,
            Category = AuditCategory.Authentication,
            Action = PlatformAuditAction,
            Outcome = AuditOutcome.Failed,
            ActorType = AuditActorType.System
        });

        await db.SaveChangesAsync();

        return new SeedResult(
            tenantA.Id,
            tenantB.Id,
            user.Id,
            agentIdByTenant,
            approvalIdByTenant);
    }
}
