using System.Linq.Expressions;

namespace DJ.Domain.Interfaces;

public interface IRepository<T>
{
    ValueTask<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default);

    ValueTask<T?> GetByIdAsync(object[] id, CancellationToken cancellationToken = default);

    T Add(T entity);

    Task<T> AddAsync(T entity, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default);

    void Remove(T entity);

    Task RemoveAsync(T entity, CancellationToken cancellationToken = default);

    void Update(T entity);

    Task UpdateAsync(T entity, CancellationToken cancellationToken = default);

    Task UpdateRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);

    Task<int> CountAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}