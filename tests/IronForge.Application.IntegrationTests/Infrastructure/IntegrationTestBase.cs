namespace IronForge.Application.IntegrationTests.Infrastructure;

public abstract class IntegrationTestBase
    : IAsyncLifetime
{
    protected PostgreSqlTestDatabase Database { get; }

    protected IntegrationTestBase(
        PostgreSqlTestDatabase database)
    {
        Database = database;
    }

    public async Task InitializeAsync()
    {
        await Database.ResetAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }
}