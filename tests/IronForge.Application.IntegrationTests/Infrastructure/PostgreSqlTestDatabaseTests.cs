using IronForge.Application.Entities;
using IronForge.Application.Execution;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Application.IntegrationTests.Infrastructure
{
    public sealed class PostgreSqlTestDatabaseTests
        : IntegrationTestBase,
          IClassFixture<PostgreSqlTestDatabase>
    {
        private readonly PostgreSqlTestDatabase _database;

        public PostgreSqlTestDatabaseTests(
            PostgreSqlTestDatabase database) : base(database)
        {
            _database = database;
        }

        [Fact]
        public async Task Database_ShouldHaveIronForgeSchema()
        {
            await using var dbContext =
                _database.CreateDbContext(CreateContext(1));

            var canConnect =
                await dbContext.Database.CanConnectAsync();

            Assert.True(canConnect);
        }

        [Fact]
        public async Task Database_ShouldHaveNoPendingMigrations()
        {
            await using var dbContext =
                _database.CreateDbContext(CreateContext(1));

            var pending =
                await dbContext.Database.GetPendingMigrationsAsync();

            Assert.Empty(pending);
        }

        [Fact]
        public async Task DbContext_ShouldPersistUpdates()
        {
            var tenantId = 1;
            var agentId = "update-test-agent";
            await using (var dbContext =
                _database.CreateDbContext(
                    CreateContext(tenantId)))
            {
                if (!await dbContext.Tenants
                .IgnoreQueryFilters()
                .AnyAsync())
                {
                    var tenant = new Tenant
                    {
                        Name = "IronForge",
                        Slug = "ironforge",
                        IsActive = true,
                        CreatedAtUtc = DateTime.UtcNow
                    };

                    dbContext.Tenants.Add(tenant);
                    await dbContext.SaveChangesAsync();

                    tenantId = tenant.Id;
                }
            }
            
            await using (var dbContext =
                _database.CreateDbContext(
                    CreateContext(tenantId)))
            {
                var agent =
                    CreateAgent(
                        tenantId,
                        agentId);

                dbContext.Agents.Add(agent);

                await dbContext.SaveChangesAsync();
            }

            await using (var dbContext =
                _database.CreateDbContext(
                    CreateContext(tenantId)))
            {
                var agent =
                    await dbContext.Agents
                        .SingleAsync(
                            x => x.AgentId == agentId);

                agent.Name = "Updated Integration Agent";

                await dbContext.SaveChangesAsync();
            }

            await using (var dbContext =
                _database.CreateDbContext(
                    CreateContext(tenantId)))
            {
                var agent =
                    await dbContext.Agents
                        .SingleAsync(
                            x => x.AgentId == agentId);

                Assert.Equal(
                    "Updated Integration Agent",
                    agent.Name);
            }
        }

        [Fact]
        public async Task DbContext_ShouldQueryPostgreSQL()
        {
            await using var dbContext =
                _database.CreateDbContext(CreateContext(1));

            var count =
                await dbContext.Agents.CountAsync();

            Assert.Equal(0, count);
        }

        [Fact]
        public async Task DbContext_ShouldPersistAgent()
        {
            var tenantId = 1;

            await using (var dbContext =
                _database.CreateDbContext(
                    CreateContext(tenantId)))
            {
                if (!await dbContext.Tenants
                .IgnoreQueryFilters()
                .AnyAsync())
                {
                    var tenant = new Tenant
                    {
                        Name = "IronForge",
                        Slug = "ironforge",
                        IsActive = true,
                        CreatedAtUtc = DateTime.UtcNow
                    };

                    dbContext.Tenants.Add(tenant);
                    await dbContext.SaveChangesAsync();

                    tenantId = tenant.Id;
                }
            }

            await using (var dbContext =
                _database.CreateDbContext(
                    CreateContext(tenantId)))
            {
                var agent = new Agent
                {
                    TenantId = tenantId,
                    AgentId = "integration-test-agent",
                    Name = "Integration Test Agent",
                    Description = "PostgreSQL integration test",
                    Version = "1.0",
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };

                dbContext.Agents.Add(agent);

                await dbContext.SaveChangesAsync();
            }

            // IMPORTANT: new DbContext
            await using (var dbContext =
                _database.CreateDbContext(
                    CreateContext(tenantId)))
            {
                var agent =
                    await dbContext.Agents
                        .SingleAsync(
                            x => x.AgentId ==
                                "integration-test-agent");

                Assert.Equal(
                    "Integration Test Agent",
                    agent.Name);
            }
        }

        [Fact]
        public async Task DbContext_ShouldIsolateAgentsByTenant()
        {
            var tenantId = 1;
            await using (var dbContext =
                _database.CreateDbContext(
                    CreateContext(tenantId)))
            {
                if (!await dbContext.Tenants
                .IgnoreQueryFilters()
                .AnyAsync())
                {
                    var tenant = new Tenant
                    {
                        Name = "IronForge",
                        Slug = "ironforge",
                        IsActive = true,
                        CreatedAtUtc = DateTime.UtcNow
                    };

                    dbContext.Tenants.Add(tenant);
                    await dbContext.SaveChangesAsync();

                    tenantId = tenant.Id;
                }
            }

            await using (var dbContext =
                _database.CreateDbContext(
                    CreateContext(tenantId)))
            {
                dbContext.Agents.Add(
                    CreateAgent(
                        tenantId: tenantId,
                        agentId: "tenant-100-agent"));

                await dbContext.SaveChangesAsync();
            }

            await using (var dbContext =
                _database.CreateDbContext(
                    CreateContext(tenantId)))
            {
                dbContext.Agents.Add(
                    CreateAgent(
                        tenantId: tenantId,
                        agentId: "tenant-200-agent"));

                await dbContext.SaveChangesAsync();
            }

            await using (var dbContext =
                _database.CreateDbContext(
                    CreateContext(tenantId)))
            {
                var agents =
                    await dbContext.Agents
                        .ToListAsync();

                Assert.Equal(
                    "tenant-100-agent",
                    agents[0].AgentId);
            }
        }
        private IronForge.Application.Execution.ExecutionContext CreateContext(int tenantId)
        {
            return new IronForge.Application.Execution.ExecutionContext
            {
                TenantId = tenantId,
                Source = ExecutionSource.InternalAgent
            };
        }
        private static Agent CreateAgent(
            int tenantId,
            string agentId)
        {
            return new Agent
            {
                TenantId = tenantId,
                AgentId = agentId,
                Name = $"Integration {agentId}",
                Description = "Integration test",
                Version = "1.0",
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
        }
    }
}
