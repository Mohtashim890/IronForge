using IronForge.Application.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Infrastructure.Data
{
    public class IronForgeDbContextFactory : IDesignTimeDbContextFactory<IronForgeDbContext>
    {
        public IronForgeDbContext CreateDbContext(string[] args)
        {
            string? connectionString = args
                .FirstOrDefault(arg => arg.StartsWith("ConnString=", StringComparison.OrdinalIgnoreCase))
                ?.Split('=')[1];

            connectionString ??= Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");

            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException(
                    "Connection string was not provided. Use: dotnet ef migrations add <Name> -- ConnString=\"your_connection_string\"");
            }

            var optionsBuilder = new DbContextOptionsBuilder<IronForgeDbContext>();

            optionsBuilder.UseNpgsql(connectionString);

            IExecutionContextAccessor dummyContext = null!;
            return new IronForgeDbContext(optionsBuilder.Options, dummyContext);
        }
    }
}
