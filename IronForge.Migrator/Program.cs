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
//   IronForge.Migrator --reset-two-factor <email>
//                                            ... remove an account's authenticator (lost phone and
//                                                recovery codes); it sets a new one at its next sign-in
// Seeding and resetting refuse to run in Production unless --force is given.

var seedDemo = args.Contains("--seed-demo", StringComparer.OrdinalIgnoreCase);
var reset = args.Contains("--reset", StringComparer.OrdinalIgnoreCase);
var force = args.Contains("--force", StringComparer.OrdinalIgnoreCase);
var resetTwoFactorIndex = Array.FindIndex(args, x => string.Equals(x, "--reset-two-factor", StringComparison.OrdinalIgnoreCase));
var resetTwoFactorEmail = resetTwoFactorIndex >= 0 && resetTwoFactorIndex + 1 < args.Length
    ? args[resetTwoFactorIndex + 1].Trim().ToLowerInvariant()
    : null;

if (resetTwoFactorIndex >= 0 && string.IsNullOrWhiteSpace(resetTwoFactorEmail))
{
    Console.Error.WriteLine("Usage: IronForge.Migrator --reset-two-factor <email>");

    return 1;
}

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

    if (resetTwoFactorEmail is not null)
    {
        return await ResetTwoFactorAsync(dbContext, resetTwoFactorEmail, logger);
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

// The operator verified the person (e.g. by phone or in person) before running this.
static async Task<int> ResetTwoFactorAsync(IronForgeDbContext dbContext, string email, ILogger logger)
{
    var user = await dbContext.Users.FirstOrDefaultAsync(x => x.Email.ToLower() == email);

    if (user is null)
    {
        logger.LogError("No account has the email {Email}.", email);

        return 3;
    }

    IronForge.Application.Auth.Services.TwoFactorService.ClearTwoFactor(user);

    // End every session and lift a lockout.
    user.SecurityStamp = User.NewSecurityStamp();
    user.FailedSignInCount = 0;
    user.LockoutEndUtc = null;

    var now = DateTime.UtcNow;

    foreach (var token in await dbContext.RefreshTokens.Where(x => x.UserId == user.Id && x.RevokedAtUtc == null).ToListAsync())
    {
        token.RevokedAtUtc = now;
    }

    dbContext.AuditEvents.Add(new AuditEvent
    {
        Category = IronForge.Application.Auditing.Models.AuditCategory.Authentication,
        Action = "TwoFactorReset",
        Outcome = IronForge.Application.Auditing.Models.AuditOutcome.Succeeded,
        ActorType = IronForge.Application.Auditing.Models.AuditActorType.System,
        ResourceType = "User",
        ResourceId = user.Id.ToString(),
        Reason = "Reset by a platform operator (IronForge.Migrator).",
        OccurredAtUtc = now
    });

    await dbContext.SaveChangesAsync();

    logger.LogInformation(
        "Two-factor sign-in was removed for {Email}. Their sessions ended; they set up an authenticator again at their next sign-in.",
        email);

    return 0;
}
