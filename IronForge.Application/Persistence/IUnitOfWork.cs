namespace IronForge.Application.Persistence;

/// <summary>
/// Runs several repository saves as one database transaction. All
/// repositories in a request share the same DbContext, so every
/// SaveChangesAsync inside <paramref name="action"/> commits or rolls back together.
/// </summary>
public interface IUnitOfWork
{
    Task<T> ExecuteInTransactionAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken = default);
}
