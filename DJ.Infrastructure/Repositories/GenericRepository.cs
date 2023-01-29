using System.Linq.Expressions;
using DJ.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DJ.Infrastructure.Repositories;

internal abstract class GenericRepository<T> : IRepository<T>
    where T : class
{
    private readonly DbContext _dbContext;

    protected GenericRepository(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    protected abstract DbSet<T> Set { get; }

    public ValueTask<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
    {
        return this.GetByIdAsync(new[] { id }, cancellationToken);
    }

    public ValueTask<T?> GetByIdAsync(object[] id, CancellationToken cancellationToken = default)
    {
        return this.Set.FindAsync(id, cancellationToken);
    }

    public T Add(T entity)
    {
        var entityEntry = this.Set.Add(entity);

        return entityEntry.Entity;
    }

    public async Task<T> AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        var entityEntry = await this.Set.AddAsync(entity, cancellationToken);

        return entityEntry.Entity;
    }

    public Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
    {
        return this.Set.AddRangeAsync(entities, cancellationToken);
    }

    public void Remove(T entity)
    {
        this.Set.Remove(entity);
    }

    public Task RemoveAsync(T entity, CancellationToken cancellationToken = default)
    {
        this.Set.Remove(entity);

        return Task.CompletedTask;
    }

    public void Update(T entity)
    {
        this.Set.Update(entity);
    }

    public Task UpdateAsync(T entity, CancellationToken cancellationToken = default)
    {
        this.Set.Update(entity);

        return Task.CompletedTask;
    }

    public Task UpdateRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
    {
        this.Set.UpdateRange(entities);

        return Task.CompletedTask;
    }

    public Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return this.Set.AnyAsync(predicate, cancellationToken);
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return this.Set.CountAsync(cancellationToken);
    }

    public Task<int> CountAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return this.Set.CountAsync(predicate, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return this._dbContext.SaveChangesAsync(cancellationToken);
    }
}