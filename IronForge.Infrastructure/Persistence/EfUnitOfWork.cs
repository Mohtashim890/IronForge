using IronForge.Application.Persistence;
using IronForge.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace IronForge.Infrastructure.Persistence;

public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly IronForgeDbContext _dbContext;

    public EfUnitOfWork(IronForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        // Already inside a transaction: join it.
        if (_dbContext.Database.CurrentTransaction != null)
        {
            return await action();
        }

        // The execution strategy keeps this compatible with retrying providers.
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction =
                await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                var result = await action();

                await transaction.CommitAsync(cancellationToken);

                return result;
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);

                // Entities added before the failure would otherwise be saved
                // by the next SaveChanges in this request.
                _dbContext.ChangeTracker.Clear();

                throw;
            }
        });
    }
}
