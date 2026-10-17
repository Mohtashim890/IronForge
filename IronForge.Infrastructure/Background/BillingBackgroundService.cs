using IronForge.Application.Execution;
using IronForge.Application.Gym.Billing;
using IronForge.Application.Gym.Members;
using IronForge.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IronForge.Infrastructure.Background;

public sealed class BillingJobOptions
{
    public const string SectionName = "BillingJob";

    public bool Enabled { get; set; } = true;

    public int IntervalMinutes { get; set; } = 60;
}

/// <summary>
/// Runs the billing rules (invoices, overdue, expiry, reminders) for every
/// gym that currently has access, one gym per DI scope under that gym's
/// tenant context. One API instance runs it at a time (advisory lock).
/// Every rule is idempotent, so running more often than daily is safe.
/// </summary>
public sealed class BillingBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _clock;
    private readonly BillingJobOptions _options;
    private readonly ILogger<BillingBackgroundService> _logger;

    public BillingBackgroundService(
        IServiceScopeFactory scopes,
        TimeProvider clock,
        IOptions<BillingJobOptions> options,
        ILogger<BillingBackgroundService> logger)
    {
        _scopes = scopes;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        // Let the API start before the first run.
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes)));

        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Billing run failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var lockScope = _scopes.CreateScope();
        var lockDb = lockScope.ServiceProvider.GetRequiredService<IronForgeDbContext>();

        var ran = await DistributedLock.TryRunAsync(lockDb, DistributedLock.BillingJob, async () =>
        {
            var directory = lockScope.ServiceProvider.GetRequiredService<IGymDirectory>();
            var tenantIds = await directory.ListServedTenantIdsAsync(_clock.GetUtcNow().UtcDateTime, cancellationToken);

            foreach (var tenantId in tenantIds)
            {
                await RunForTenantAsync(tenantId, cancellationToken);
            }
        }, cancellationToken);

        if (!ran)
        {
            _logger.LogDebug("Billing run skipped: another instance holds the lock.");
        }
    }

    private async Task RunForTenantAsync(int tenantId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopes.CreateScope();

            scope.ServiceProvider
                .GetRequiredService<IExecutionContextAccessor>()
                .Set(new IronForge.Application.Execution.ExecutionContext
                {
                    TenantId = tenantId,
                    Source = ExecutionSource.Background
                });

            var result = await scope.ServiceProvider
                .GetRequiredService<IBillingEngine>()
                .RunAsync(tenantId, cancellationToken);

            if (result.InvoicesIssued + result.InvoicesOverdue + result.MembersExpired + result.RemindersSent > 0)
            {
                _logger.LogInformation(
                    "Billing for tenant {TenantId}: {Issued} issued, {Overdue} overdue, {Expired} expired, {Unfrozen} unfrozen, {Reminders} reminders.",
                    tenantId,
                    result.InvoicesIssued,
                    result.InvoicesOverdue,
                    result.MembersExpired,
                    result.MembersUnfrozen,
                    result.RemindersSent);
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // One gym's failure must not stop the others.
            _logger.LogError(ex, "Billing for tenant {TenantId} failed.", tenantId);
        }
    }
}
