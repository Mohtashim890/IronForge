using System.Linq.Expressions;

namespace IronForge.Application.Persistence;

public interface IRepository<TEntity>
    where TEntity : class
{
    Task<TEntity?> FindAsync(
        params object[] keyValues);

    Task<TEntity?> FirstOrDefaultAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<TEntity?> FirstOrDefaultTrackedAsync(
    Expression<Func<TEntity, bool>> predicate,
    CancellationToken cancellationToken = default);

    Task<List<TEntity>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<List<TEntity>> ListAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<List<TEntity>> ListTrackedAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<int> CountAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a composed query (filter, order, page, project) in the database.
    /// The query is built from LINQ operators only, so the Application layer
    /// stays free of EF Core; tenant query filters still apply. No tracking.
    /// </summary>
    Task<List<TResult>> QueryAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>> query,
        CancellationToken cancellationToken = default);

    /// <summary>Like <see cref="QueryAsync{TResult}"/>, with tracked entities for updates.</summary>
    Task<List<TEntity>> QueryTrackedAsync(
        Func<IQueryable<TEntity>, IQueryable<TEntity>> query,
        CancellationToken cancellationToken = default);

    /// <summary>Counts the rows of a composed query in the database.</summary>
    Task<int> QueryCountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>> query,
        CancellationToken cancellationToken = default);

    /// <summary>Sums a projected amount in the database; 0 when there are no rows.</summary>
    Task<decimal> QuerySumAsync(
        Func<IQueryable<TEntity>, IQueryable<decimal>> query,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        TEntity entity,
        CancellationToken cancellationToken = default);

    void Add(TEntity entity);

    void Update(TEntity entity);

    void Remove(TEntity entity);

    Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default);
}