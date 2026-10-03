using System.Linq.Expressions;
using Explore.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Database;

internal static class RelationalEntityRowFence
{
    public static Task AcquireLookupAsync<TEntity>(
        ExploreDbContext dbContext, IReadOnlyList<int> keys, CancellationToken cancellationToken)
        where TEntity : class =>
        AcquirePrimaryKeyAsync<TEntity>(dbContext, keys.Cast<object>().ToArray(), cancellationToken);

    public static Task AcquireGlobalAsync<TEntity>(
        ExploreDbContext dbContext, Guid key, CancellationToken cancellationToken)
        where TEntity : class =>
        AcquirePrimaryKeyAsync<TEntity>(dbContext, [key], cancellationToken);

    private static async Task AcquirePrimaryKeyAsync<TEntity>(
        ExploreDbContext dbContext, IReadOnlyList<object> keys, CancellationToken cancellationToken)
        where TEntity : class
    {
        if (!dbContext.Database.IsRelational())
            return;
        if (dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Authority row fences require an active transaction.");
        var entity = dbContext.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException("Authority entity is not mapped.");
        var primaryKey = entity.FindPrimaryKey()
            ?? throw new InvalidOperationException("Authority entity has no primary key.");
        if (primaryKey.Properties.Count != keys.Count
            || primaryKey.Properties.Where((property, index) => property.ClrType != keys[index].GetType()).Any())
            throw new ArgumentException("Authority fences require every mapped primary key with its exact type.", nameof(keys));
        string tableName = entity.GetTableName()
            ?? throw new InvalidOperationException("Authority entity has no table mapping.");
        var store = StoreObjectIdentifier.Table(tableName, entity.GetSchema());
        var sql = dbContext.GetService<ISqlGenerationHelper>();
        string table = sql.DelimitIdentifier(tableName, entity.GetSchema());
        var columns = primaryKey.Properties.Select(property => sql.DelimitIdentifier(
            property.GetColumnName(store)
            ?? throw new InvalidOperationException("Authority key column is not mapped."))).ToArray();
        string predicate = string.Join(" AND ", columns.Select((column, index) => $"{column} = {{{index}}}"));
        // A no-op native write conflicts with ordinary permission/role updates and deletes.
        // Under PostgreSQL serializable isolation a changed snapshot aborts the whole attempt.
        await dbContext.Database.ExecuteSqlRawAsync(
            $"UPDATE {table} SET {columns[0]} = {columns[0]} WHERE {predicate}",
            keys, cancellationToken);
    }

    public static async Task AcquireAsync<TEntity>(
        ExploreDbContext dbContext,
        Guid tenantId,
        Expression<Func<TEntity, Guid>> keyPropertyExpression,
        Guid key,
        CancellationToken cancellationToken)
        where TEntity : class, ITenantEntity
    {
        ArgumentNullException.ThrowIfNull(keyPropertyExpression);
        if (!dbContext.Database.IsRelational())
        {
            return;
        }

        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Admission authority row fences require an active unit-of-work transaction.");
        }

        string providerName = dbContext.Database.ProviderName
            ?? throw new InvalidOperationException("Admission authority requires a relational provider.");
        IEntityType entityType = dbContext.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException(
                $"Admission authority entity '{typeof(TEntity).Name}' is not mapped.");
        IProperty keyProperty = ResolveProperty(entityType, keyPropertyExpression);
        IProperty tenantProperty = entityType.FindProperty(nameof(ITenantEntity.TenantId))
            ?? throw new InvalidOperationException(
                $"Admission authority entity '{typeof(TEntity).Name}' has no tenant property.");

        if (providerName == RelationalNamedLock.SqliteProvider)
        {
            await dbContext.Set<TEntity>()
                .Where(entity =>
                    entity.TenantId == tenantId &&
                    EF.Property<Guid>(entity, keyProperty.Name) == key)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(keyPropertyExpression, keyPropertyExpression),
                    cancellationToken);
            return;
        }

        string tableName = entityType.GetTableName()
            ?? throw new InvalidOperationException(
                $"Admission authority entity '{typeof(TEntity).Name}' has no table mapping.");
        var storeObject = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());
        ISqlGenerationHelper sql = dbContext.GetService<ISqlGenerationHelper>();
        string table = sql.DelimitIdentifier(tableName, entityType.GetSchema());
        string tenantColumn = sql.DelimitIdentifier(
            tenantProperty.GetColumnName(storeObject)
            ?? throw new InvalidOperationException("Admission authority tenant column is not mapped."));
        string keyColumn = sql.DelimitIdentifier(
            keyProperty.GetColumnName(storeObject)
            ?? throw new InvalidOperationException("Admission authority key column is not mapped."));

        string command = providerName switch
        {
            RelationalNamedLock.PostgreSqlProvider or RelationalNamedLock.MySqlProvider =>
                $"SELECT {keyColumn} FROM {table} WHERE {tenantColumn} = {{0}} " +
                $"AND {keyColumn} = {{1}} FOR UPDATE",
            RelationalNamedLock.SqlServerProvider =>
                $"SELECT {keyColumn} FROM {table} WITH (UPDLOCK, HOLDLOCK) " +
                $"WHERE {tenantColumn} = {{0}} AND {keyColumn} = {{1}}",
            _ => throw new InvalidOperationException(
                $"Unsupported admission authority provider '{providerName}'."),
        };
        await dbContext.Database.ExecuteSqlRawAsync(
            command,
            [tenantId, key],
            cancellationToken);
    }

    private static IProperty ResolveProperty<TEntity>(
        IEntityType entityType,
        Expression<Func<TEntity, Guid>> expression)
        where TEntity : class, ITenantEntity
    {
        if (expression.Body is not MemberExpression member ||
            member.Expression != expression.Parameters[0])
        {
            throw new ArgumentException(
                "Admission authority fences require a direct mapped property expression.",
                nameof(expression));
        }

        return entityType.FindProperty(member.Member.Name)
            ?? throw new InvalidOperationException(
                $"Admission authority property '{member.Member.Name}' is not mapped.");
    }
}
