using IronForge.Application.Agents.Approvals;
using IronForge.Application.Agents.Authorization;
using IronForge.Application.Agents.Delegations;
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

            var clientAuthorizations =
                await db.AgentMcpClientAuthorizations.ToListAsync();
            Assert.Single(clientAuthorizations);
            Assert.All(clientAuthorizations, x => Assert.Equal(tenantId, x.TenantId));

            var delegations = await db.AgentDelegations.ToListAsync();
            Assert.Single(delegations);
            Assert.All(delegations, x => Assert.Equal(tenantId, x.TenantId));

            var scopes = await db.AgentDelegationScopes.ToListAsync();
            Assert.Single(scopes);
            Assert.Equal(delegations[0].DelegationId, scopes[0].DelegationId);

            // Direct lookups of the other tenant's rows by key find nothing.
            Assert.False(await db.AgentApprovals
                .AnyAsync(x => x.ApprovalId == seed.ApprovalIdByTenant[other]));

            Assert.False(await db.AgentDelegations
                .AnyAsync(x => x.DelegationId == seed.DelegationIdByTenant[other]));
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
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.AgentMcpClientAuthorizations.ToListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.AgentDelegations.ToListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.AgentDelegationScopes.ToListAsync());
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
    public async Task DelegationStore_CannotReadOtherTenantsDelegations()
    {
        var seed = await SeedTwoTenantsAsync();

        await using var db = CreateDbContext(seed.TenantA);

        var store = new DBAgentDelegationStore(
            new EfRepository<AgentDelegationEntity>(db),
            new EfRepository<AgentDelegationScopeEntity>(db));

        var own = await store.GetAsync(
            seed.DelegationIdByTenant[seed.TenantA]);

        Assert.NotNull(own);
        Assert.Equal(seed.TenantA, own!.TenantId);
        Assert.Single(own.Scopes);

        Assert.Null(await store.GetAsync(
            seed.DelegationIdByTenant[seed.TenantB]));

        // The user has a delegation in both tenants; only tenant A's is listed.
        var listed = await store.GetForUserAsync(seed.UserId);

        Assert.Single(listed);
        Assert.Equal(seed.TenantA, listed[0].TenantId);

        // Revoking another tenant's delegation is a no-op.
        Assert.Equal(
            Guid.Empty,
            await store.RevokeAsync(seed.DelegationIdByTenant[seed.TenantB]));
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

    [Fact]
    public async Task McpClientAuthorization_IsOnlyGrantedInsideOwnTenant()
    {
        var seed = await SeedTwoTenantsAsync();

        // Both tenants use the same agent and client identifiers, so only
        // the tenant filter tells their authorizations apart.
        await using (var dbA = CreateDbContext(seed.TenantA))
        {
            Assert.True(await CreateClientAuthorizationService(dbA)
                .IsAuthorizedAsync(SharedAgentId, SharedClientId));
        }

        await using (var dbA = CreateDbContext(seed.TenantA))
        {
            // Remove tenant A's grant; tenant B's grant must not leak in.
            var authorization =
                await dbA.AgentMcpClientAuthorizations.SingleAsync();

            authorization.IsActive = false;

            await dbA.SaveChangesAsync();
        }

        await using (var dbA = CreateDbContext(seed.TenantA))
        {
            Assert.False(await CreateClientAuthorizationService(dbA)
                .IsAuthorizedAsync(SharedAgentId, SharedClientId));
        }

        await using (var dbB = CreateDbContext(seed.TenantB))
        {
            Assert.True(await CreateClientAuthorizationService(dbB)
                .IsAuthorizedAsync(SharedAgentId, SharedClientId));
        }
    }

    private const string SharedAgentId = "shared-agent";
    private const string SharedClientId = "shared-client";
    private const string PlatformAuditAction = "PlatformEvent";

    private sealed record SeedResult(
        int TenantA,
        int TenantB,
        int UserId,
        Dictionary<int, int> AgentIdByTenant,
        Dictionary<int, string> ApprovalIdByTenant,
        Dictionary<int, Guid> DelegationIdByTenant);

    private IronForgeDbContext CreateDbContext(int? tenantId)
    {
        return Database.CreateDbContext(
            new IronForge.Application.Execution.ExecutionContext
            {
                TenantId = tenantId,
                Source = ExecutionSource.Web
            });
    }

    private static AgentMcpClientAuthorizationService
        CreateClientAuthorizationService(IronForgeDbContext db)
    {
        return new AgentMcpClientAuthorizationService(
            new EfRepository<AgentMcpClientAuthorization>(db),
            new EfRepository<Agent>(db),
            new EfRepository<McpClient>(db));
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
        var delegationIdByTenant = new Dictionary<int, Guid>();

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

            var client = new McpClient
            {
                TenantId = tenant.Id,
                ClientId = SharedClientId,
                Name = $"Client {tenant.Slug}",
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            db.Agents.Add(agent);
            db.McpClients.Add(client);

            await db.SaveChangesAsync();

            agentIdByTenant[tenant.Id] = agent.Id;

            db.AgentMcpClientAuthorizations.Add(new AgentMcpClientAuthorization
            {
                TenantId = tenant.Id,
                AgentEntityId = agent.Id,
                McpClientEntityId = client.Id,
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });

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
                ToolName = "create_product",
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
                Category = AuditCategory.Product,
                Action = $"Event-{tenant.Slug}",
                Outcome = AuditOutcome.Succeeded,
                ActorType = AuditActorType.User,
                UserId = user.Id
            });

            var delegationId = Guid.NewGuid();
            delegationIdByTenant[tenant.Id] = delegationId;

            db.AgentDelegations.Add(new AgentDelegationEntity
            {
                DelegationId = delegationId,
                TenantId = tenant.Id,
                UserId = user.Id,
                AgentId = SharedAgentId,
                ClientId = SharedClientId,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddMinutes(30),
                Scopes =
                {
                    new AgentDelegationScopeEntity
                    {
                        DelegationId = delegationId,
                        Scope = "products.read"
                    }
                }
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
            approvalIdByTenant,
            delegationIdByTenant);
    }
}
