using IronForge.Application.Execution;
using IronForge.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var connectionString =
    Environment.GetEnvironmentVariable(
        "ConnectionStrings__DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine(
        "ConnectionStrings__DefaultConnection is not configured.");

    return 1;
}

var services = new ServiceCollection();

services.AddScoped<IExecutionContextAccessor, ExecutionContextAccessor>();
services.AddLogging(builder =>
{
    builder.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
    });
});

services.AddDbContext<IronForgeDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

await using var serviceProvider = services.BuildServiceProvider();

var logger =
    serviceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("IronForge.Migrations");

try
{
    logger.LogInformation(
        "Starting database migration.");

    await using var scope =
        serviceProvider.CreateAsyncScope();

    var dbContext =
        scope.ServiceProvider
            .GetRequiredService<IronForgeDbContext>();

    var pendingMigrations =
        await dbContext.Database
            .GetPendingMigrationsAsync();

    var pending = pendingMigrations.ToList();

    if (pending.Count == 0)
    {
        logger.LogInformation(
            "Database is already up to date.");

        return 0;
    }

    logger.LogInformation(
        "Applying {MigrationCount} pending migration(s).",
        pending.Count);

    foreach (var migration in pending)
    {
        logger.LogInformation(
            "Pending migration: {Migration}",
            migration);
    }

    await dbContext.Database.MigrateAsync();

    logger.LogInformation(
        "Database migration completed successfully.");

    return 0;
}
catch (Exception ex)
{
    logger.LogError(
        ex,
        "Database migration failed.");

    return 1;
}