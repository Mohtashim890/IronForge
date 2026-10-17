using IronForge.Application.Entities;
using IronForge.Infrastructure.Data;
using IronForge.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IronForge.Infrastructure.Background;

/// <summary>
/// Delivers queued emails with retries and exponential backoff. A message
/// that keeps failing is marked Failed after <see cref="MaximumAttempts"/>.
/// </summary>
public sealed class EmailOutboxProcessor : BackgroundService
{
    public const int MaximumAttempts = 6;
    private const int BatchSize = 25;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _clock;
    private readonly ILogger<EmailOutboxProcessor> _logger;

    public EmailOutboxProcessor(
        IServiceScopeFactory scopes,
        TimeProvider clock,
        ILogger<EmailOutboxProcessor> logger)
    {
        _scopes = scopes;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        do
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Email outbox run failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<IronForgeDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        var sent = 0;

        await DistributedLock.TryRunAsync(db, DistributedLock.EmailOutbox, async () =>
        {
            var now = _clock.GetUtcNow().UtcDateTime;

            var due = await db.EmailOutbox
                .Where(x => x.Status == EmailStatus.Pending && x.NextAttemptAtUtc <= now)
                .OrderBy(x => x.CreatedAtUtc)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            foreach (var message in due)
            {
                message.Attempts++;

                try
                {
                    await sender.SendAsync(
                        new OutgoingEmail(message.ToEmail, message.ToName, message.Subject, message.HtmlBody),
                        cancellationToken);

                    message.Status = EmailStatus.Sent;
                    message.SentAtUtc = _clock.GetUtcNow().UtcDateTime;
                    message.LastError = null;
                    sent++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    message.LastError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;

                    if (message.Attempts >= MaximumAttempts)
                    {
                        message.Status = EmailStatus.Failed;

                        _logger.LogError(ex, "Email {EmailId} to {ToEmail} failed permanently.", message.Id, message.ToEmail);
                    }
                    else
                    {
                        // 1, 2, 4, 8, 16 minutes.
                        message.NextAttemptAtUtc = now.AddMinutes(Math.Pow(2, message.Attempts - 1));

                        _logger.LogWarning(ex, "Email {EmailId} attempt {Attempt} failed.", message.Id, message.Attempts);
                    }
                }

                await db.SaveChangesAsync(cancellationToken);
            }
        }, cancellationToken);

        return sent;
    }
}
