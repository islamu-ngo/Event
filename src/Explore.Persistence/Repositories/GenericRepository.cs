using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Npgsql;

namespace Explore.Persistence.Repositories;

public class GenericRepository<T, TKey> : IGenericRepository<T, TKey> where T : class
{
    private readonly ExploreDbContext _dbContext;

    public GenericRepository(ExploreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public virtual async Task<T> Create(T entity)
    {
        RequireTenantMutationScope(entity);
        try
        {
            await _dbContext.AddAsync(entity);
            await _dbContext.SaveChangesAsync();
            return entity;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx && pgEx.SqlState == "23505")
        {
            // Duplicate key violation - detach the entity and rethrow with more context
            _dbContext.Entry(entity).State = EntityState.Detached;
            throw new InvalidOperationException(
                $"A record with the same unique key already exists. Constraint: {pgEx.ConstraintName}. " +
                $"Detail: {pgEx.Detail}",
                ex);
        }
    }

    /// <summary>
    /// Deletes an entity. If entity implements ISoftDeletable, performs soft delete (sets IsDeleted=true).
    /// Otherwise performs hard delete (permanent removal from database).
    /// </summary>
    public virtual async Task Delete(T entity)
    {
        RequireTenantMutationScope(entity);
        // Check if entity supports soft delete
        if (entity is ISoftDeletable)
        {
            var cascadeTiming = _dbContext.ChangeTracker.CascadeDeleteTiming;
            try
            {
                // Convert to a soft delete before EF processes required relationships.
                _dbContext.ChangeTracker.CascadeDeleteTiming = CascadeTiming.OnSaveChanges;
                _dbContext.Entry(entity).State = EntityState.Deleted;
                await _dbContext.SaveChangesAsync();
            }
            finally
            {
                _dbContext.ChangeTracker.CascadeDeleteTiming = cascadeTiming;
            }
        }
        else
        {
            // Hard delete: Permanently remove from database
            _dbContext.Set<T>().Remove(entity);
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task<bool> Exists(TKey id)
    {
        return await VisibleRows().AnyAsync(KeyPredicate(id));
    }

    private Expression<Func<T, bool>> KeyPredicate(TKey id)
    {
        var entityType = _dbContext.Model.FindEntityType(typeof(T))
            ?? throw new InvalidOperationException($"Entity type metadata not found for {typeof(T).Name}.");
        var primaryKey = entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException($"Primary key metadata not found for {typeof(T).Name}.");

        if (primaryKey.Properties.Count != 1)
        {
            throw new InvalidOperationException($"{typeof(T).Name} must have a single-column primary key to use {nameof(Exists)}.");
        }

        var keyProperty = primaryKey.Properties[0];
        var entity = Expression.Parameter(typeof(T), "entity");
        var property = Expression.Call(
            typeof(EF),
            nameof(EF.Property),
            [keyProperty.ClrType],
            entity,
            Expression.Constant(keyProperty.Name));
        Expression<Func<TKey>> selectedId = () => id;
        var keyValue = Expression.Convert(selectedId.Body, keyProperty.ClrType);
        return Expression.Lambda<Func<T, bool>>(
            Expression.Equal(property, keyValue),
            entity);
    }

    public async Task<IReadOnlyList<T>> GetAll()
    {
        return await VisibleRows().ToListAsync();
    }

    public async Task<(IReadOnlyList<T> Items, int TotalCount)> GetAllPaged(int pageNumber, int pageSize)
    {
        var query = VisibleRows();
        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        return (items, totalCount);
    }

    public async Task<T?> GetById(TKey id)
    {
        // FindAsync can return an identity-map entry without applying tenant or
        // soft-delete filters. Reads must reflect the persisted row, not local edits.
        return await VisibleRows().SingleOrDefaultAsync(KeyPredicate(id));
    }

    public virtual async Task Update(T entity)
    {
        RequireTenantMutationScope(entity);
        var entry = _dbContext.Entry(entity);
        if (entry.State != EntityState.Detached)
        {
            entry.State = EntityState.Modified;
            await _dbContext.SaveChangesAsync();
            return;
        }

        var entityType = _dbContext.Model.FindEntityType(typeof(T))
            ?? throw new InvalidOperationException($"Entity type metadata not found for {typeof(T).Name}.");
        var primaryKey = entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException($"Primary key metadata not found for {typeof(T).Name}.");

        var trackedEntity = _dbContext.Set<T>().Local.FirstOrDefault(localEntity => HasSamePrimaryKey(localEntity, entity, primaryKey.Properties));

        if (trackedEntity is not null)
        {
            _dbContext.Entry(trackedEntity).CurrentValues.SetValues(entity);
            _dbContext.Entry(trackedEntity).State = EntityState.Modified;
        }
        else
        {
            entry.State = EntityState.Modified;
        }

        await _dbContext.SaveChangesAsync();
    }

    private IQueryable<T> VisibleRows()
    {
        var query = _dbContext.Set<T>().AsNoTracking();
        var tenant = _dbContext.Model.FindEntityType(typeof(T))?.FindProperty("TenantId");
        if (tenant is null || tenant.ClrType != typeof(Guid) && tenant.ClrType != typeof(Guid?))
            return query;

        Guid? tenantId = _dbContext.TenantFilterTenantId;
        if (tenantId == Guid.Empty)
            tenantId = null;
        var entity = Expression.Parameter(typeof(T), "entity");
        var column = Expression.Call(typeof(EF), nameof(EF.Property), [tenant.ClrType],
            entity, Expression.Constant(tenant.Name));
        Expression<Func<Guid?>> selectedTenant = () => tenantId;
        Expression predicate = Expression.Equal(Expression.Convert(column, typeof(Guid?)), selectedTenant.Body);
        if (tenant.ClrType == typeof(Guid?))
            predicate = Expression.OrElse(Expression.Equal(column, Expression.Constant(null, typeof(Guid?))), predicate);
        return query.Where(Expression.Lambda<Func<T, bool>>(predicate, entity));
    }

    private void RequireTenantMutationScope(T entity)
    {
        var entry = _dbContext.Entry(entity);
        var tenant = entry.Metadata.FindProperty("TenantId");
        if (tenant is not null && (tenant.ClrType == typeof(Guid) || tenant.ClrType == typeof(Guid?))
            && entry.Property(tenant.Name).CurrentValue is Guid targetTenant
            && (targetTenant == Guid.Empty || _dbContext.TenantFilterTenantId != targetTenant
                || _dbContext.IsTenantFilterBypassed))
            throw new InvalidOperationException("tracked_tenant_authority_mismatch");
    }

    private bool HasSamePrimaryKey(T trackedEntity, T candidateEntity, IReadOnlyList<Microsoft.EntityFrameworkCore.Metadata.IProperty> keyProperties)
    {
        foreach (var property in keyProperties)
        {
            var trackedValue = property.PropertyInfo?.GetValue(trackedEntity);
            var candidateValue = property.PropertyInfo?.GetValue(candidateEntity);

            if (!Equals(trackedValue, candidateValue))
            {
                return false;
            }
        }

        return true;
    }
}
