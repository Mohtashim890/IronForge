using IronForge.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace IronForge.Application.IntegrationTests.Infrastructure;

public sealed class PostgreSqlTestDatabase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder()
            .WithDatabase("ironforge_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

    private IntegrationTestDbContextFactory? _dbContextFactory;
    private PostgreSqlDatabaseResetter? _resetter;

    public string ConnectionString =>
        _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        _dbContextFactory =
            new IntegrationTestDbContextFactory(
                ConnectionString);

        await ApplyMigrationsAsync();

        _resetter =
       new PostgreSqlDatabaseResetter(
           ConnectionString);

    }

    public async Task ResetAsync(
    CancellationToken cancellationToken = default)
    {
        if (_resetter == null)
        {
            throw new InvalidOperationException(
                "PostgreSQL test database has not been initialized.");
        }

        await _resetter.ResetAsync(cancellationToken);
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
    public IronForgeDbContext CreateDbContext()
    {
        var options =
            new DbContextOptionsBuilder<IronForgeDbContext>()
                .UseNpgsql(ConnectionString)
                .Options;

        var executionContext =
            new TestExecutionContextAccessor();

        return new IronForgeDbContext(
            options,
            executionContext);
    }

    public IronForgeDbContext CreateDbContext(
        IronForge.Application.Execution.ExecutionContext executionContext)
    {
        if (_dbContextFactory == null)
        {
            throw new InvalidOperationException(
                "PostgreSQL test database has not been initialized.");
        }

        return _dbContextFactory.CreateDbContext(
            executionContext);
    }

    private async Task ApplyMigrationsAsync()
    {
        await using var dbContext = CreateDbContext();

        await dbContext.Database.MigrateAsync();
    }
}