using IronForge.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Application.IntegrationTests.Infrastructure
{
    public sealed class IntegrationTestDbContextFactory
    {
        private readonly string _connectionString;

        public IntegrationTestDbContextFactory(
            string connectionString)
        {
            _connectionString = connectionString;
        }

        public IronForgeDbContext CreateDbContext(IronForge.Application.Execution.ExecutionContext executionContext)
        {
            var options =
                new DbContextOptionsBuilder<IronForgeDbContext>()
                    .UseNpgsql(_connectionString)
                    .Options;

            var executionContextAccessor =
                new TestExecutionContextAccessor();

            executionContextAccessor.Set(executionContext);

            return new IronForgeDbContext(
                options,
                executionContextAccessor);
        }
    }
}
