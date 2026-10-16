using IronForge.Application.Agents.Models;
using IronForge.Application.Agents.Runtime;
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
                    // Platform operator; tenant access comes from the membership below.
                    Role = PlatformRoles.PlatformAdmin,
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
                    Role = TenantRoles.Admin,
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow
                };

                dbContext.TenantMemberships.Add(membership);
                await dbContext.SaveChangesAsync();
            }

            if (!await dbContext.Agents.IgnoreQueryFilters().AnyAsync())
            {
                foreach (var template in AgentTemplates.Default)
                {
                    await SeedAgentAsync(
                        dbContext,
                        tenantId,
                        template);
                }
            }
        }

        /// <summary>
        /// Installs a template agent (managed, with its tool bindings) unless
        /// the tenant already has an agent with that id. Idempotent.
        /// </summary>
        private static async Task SeedAgentAsync(
            IronForgeDbContext dbContext,
            int tenantId,
            AgentTemplate template)
        {
            var exists = await dbContext.Agents
                .IgnoreQueryFilters()
                .AnyAsync(x =>
                    x.TenantId == tenantId &&
                    x.AgentId == template.TemplateId);

            if (exists)
            {
                return;
            }

            dbContext.Agents.Add(template.CreateAgent(tenantId, DateTime.UtcNow));

            await dbContext.SaveChangesAsync();
        }
    }
}
