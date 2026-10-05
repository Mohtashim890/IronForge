using IronForge.Application.Agents.Models;
using IronForge.Application.Entities;
using IronForge.Shared.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IronForge.Infrastructure.Data
{
    public class DbSeeder
    {
        public static async Task SeedAsync(
        IServiceProvider services)
        {
            using var scope =
                services.CreateScope();

            var dbContext =
                scope.ServiceProvider
                    .GetRequiredService<IronForgeDbContext>();

            var passwordHasher =
                scope.ServiceProvider
                    .GetRequiredService<
                        IPasswordHasher<User>>();

            int tenantId = 0;
            int usertId = 0;

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

            if (!await dbContext.Users
                .IgnoreQueryFilters()
                .AnyAsync())
            {
                var user = new User
                {
                    Username = "admin",
                    Email = "admin@ironforge.local",
                    Role = "Admin",
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow,
                };

                user.PasswordHash =
                    passwordHasher.HashPassword(
                        user,
                        "Password123!");

                dbContext.Users.Add(user);
                await dbContext.SaveChangesAsync();
                usertId = user.Id;

                var membership = new TenantMembership
                {
                    UserId = usertId,
                    TenantId = tenantId,
                    Role = "Admin",
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow
                };

                dbContext.TenantMemberships.Add(membership);
                await dbContext.SaveChangesAsync();
            }

            if (!await dbContext.Agents.IgnoreQueryFilters().AnyAsync())
            {
                var agent = await dbContext.Agents
              .IgnoreQueryFilters()
              .SingleOrDefaultAsync(
                  x =>
                      x.TenantId == tenantId &&
                      x.AgentId == AgentIds.ProductAgent);

                if (agent is null)
                {
                    agent = new Agent
                    {
                        TenantId = tenantId,
                        AgentId = AgentIds.ProductAgent,
                        Name = "Product Agent",
                        Description =
                            "Agent for product management operations.",
                        Version = "1.0",
                        IsActive = true,
                        CreatedAtUtc = DateTime.UtcNow,
                        UpdatedAtUtc = DateTime.UtcNow
                    };

                    dbContext.Agents.Add(agent);

                    await dbContext.SaveChangesAsync();
                }

                var permissions =
                    new[]
                    {
                        Permissions.ProductsRead,
                        Permissions.ProductsCreate,
                        Permissions.ProductsUpdate,
                        Permissions.ProductsDelete
                    };

                var existingPermissions =
                    await dbContext.AgentPermissions
                        .Where(x => x.AgentId == agent.Id)
                        .Select(x => x.Permission)
                        .ToListAsync();

                foreach (var permission in permissions)
                {
                    if (existingPermissions.Contains(permission))
                    {
                        continue;
                    }

                    dbContext.AgentPermissions.Add(
                        new AgentPermission
                        {
                            AgentId = agent.Id,
                            Permission = permission
                        });
                }

                agent.UpdatedAtUtc = DateTime.UtcNow;

                await dbContext.SaveChangesAsync();
            }
        }
    }
}
