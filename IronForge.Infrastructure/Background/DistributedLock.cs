using IronForge.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace IronForge.Infrastructure.Background;

/// <summary>
/// A PostgreSQL session advisory lock, so a background job runs on one API
/// instance at a time. The lock lives on the DbContext's connection, which
/// stays open until the lock is released.
/// </summary>
public static class DistributedLock
{
    public const long BillingJob = 7_100_001;
    public const long EmailOutbox = 7_100_002;

    /// <summary>Runs <paramref name="work"/> if the lock is free; returns false otherwise.</summary>
    public static async Task<bool> TryRunAsync(
        IronForgeDbContext dbContext,
        long key,
        Func<Task> work,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            var acquired = (await dbContext.Database
                    .SqlQuery<bool>($"SELECT pg_try_advisory_lock({key}) AS \"Value\"")
                    .ToListAsync(cancellationToken))
                .Single();

            if (!acquired)
            {
                return false;
            }

            try
            {
                await work();
                return true;
            }
            finally
            {
                await dbContext.Database
                    .SqlQuery<bool>($"SELECT pg_advisory_unlock({key}) AS \"Value\"")
                    .ToListAsync(CancellationToken.None);
            }
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }
}
