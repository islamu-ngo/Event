using System;
using System.Collections.Generic;
using System.Text;

namespace Explore.Application.Contracts.Persistence;

public interface IGenericRepository<T, TKey> where T : class
{
    Task<T?> GetById(TKey id);
    Task<IReadOnlyList<T>> GetAll();

    /// <summary>
    /// Gets a paginated list of all entities.
    /// </summary>
    /// <param name="pageNumber">The page number (1-based).</param>
    /// <param name="pageSize">The number of items per page.</param>
    /// <returns>A tuple containing the items and total count.</returns>
    Task<(IReadOnlyList<T> Items, int TotalCount)> GetAllPaged(int pageNumber, int pageSize);

    Task<bool> Exists(TKey id);
    Task<T> Create(T entity);
    Task Update(T entity);

    /// <summary>
    /// Deletes an entity. If entity implements ISoftDeletable, performs soft delete.
    /// Otherwise performs hard delete (permanent removal).
    /// </summary>
    Task Delete(T entity);

}
