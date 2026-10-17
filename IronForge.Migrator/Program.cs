using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Infrastructure.Data;
using IronForge.Infrastructure.Data.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Usage:
//   IronForge.Migrator                       apply pending migrations
//   IronForge.Migrator --seed-demo           ... then add demo data (skipped if present)
//   IronForge.Migrator --seed-demo --reset   ... wipe all data first, then seed
// Seeding and resetting refuse to run in Production unless --force is given.

var seedDemo = args.Contains("--seed-demo", StringComparer.OrdinalIgnoreCase);
var reset = args.Contains("--reset", StringComparer.OrdinalIgnoreCase);
var force = args.Contains("--force", StringComparer.OrdinalIgnoreCase);

var environment =
    Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ??
    Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ??
    "Production";

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
services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
services.AddLogging(builder =>
{
    builder.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
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

if ((seedDemo || reset) &&
    string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase) &&
    !force)
{
    logger.LogError(
        "Refusing to seed or reset in {Environment}. Set DOTNET_ENVIRONMENT=Development or pass --force.",
        environment);

    return 2;
}

try
{
    await using var scope =
        serviceProvider.CreateAsyncScope();

    var dbContext =
        scope.ServiceProvider
            .GetRequiredService<IronForgeDbContext>();

    var pending = (await dbContext.Database.GetPendingMigrationsAsync()).ToList();

    if (pending.Count == 0)
    {
        logger.LogInformation(
            "Database is already up to date.");
    }
    else
    {
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
    }

    if (reset)
    {
        logger.LogWarning("Deleting all data (--reset).");
        await DemoDataSeeder.ResetAsync(dbContext);
    }

    if (seedDemo)
    {
        logger.LogInformation("Seeding demo data.");

        var seeder = new DemoDataSeeder(
            dbContext,
            scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>(),
            logger);

        await seeder.SeedAsync();
    }

    return 0;
}
catch (Exception ex)
{
    logger.LogError(
        ex,
        "Database migration failed.");

    return 1;
}
