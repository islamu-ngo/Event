using System.Linq.Expressions;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class RelationalInstanceBootstrapStateLock
{
    public static Task<InstanceBootstrapState?> LoadCurrentAsync(
        ExploreDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            return dbContext.InstanceBootstrapStates
                .AsTracking()
                .OrderByDescending(state => state.Generation)
                .ThenByDescending(state => state.CreatedAt)
                .ThenByDescending(state => state.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        RelationalProvider provider = RelationalProviderClassifier.Classify(
            dbContext.Database);
        if (provider == RelationalProvider.Sqlite)
        {
            return dbContext.InstanceBootstrapStates
                .AsTracking()
                .OrderByDescending(state => state.Generation)
                .ThenByDescending(state => state.CreatedAt)
                .ThenByDescending(state => state.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        (string table, string generation, string createdAt, string id) =
            ResolveNames(dbContext);
        string sql = provider switch
        {
            RelationalProvider.SqlServer =>
                $"SELECT TOP(1) * FROM {table} WITH (UPDLOCK, ROWLOCK, HOLDLOCK) " +
                $"ORDER BY {generation} DESC, {createdAt} DESC, {id} DESC",
            RelationalProvider.PostgreSql or RelationalProvider.MySql =>
                $"SELECT * FROM {table} ORDER BY {generation} DESC, {createdAt} DESC, {id} DESC LIMIT 1 FOR UPDATE",
            _ => throw new InvalidOperationException(
                $"Bootstrap row locking is unavailable for provider '{provider}'."),
        };

        return dbContext.InstanceBootstrapStates
            .FromSqlRaw(sql)
            .AsTracking()
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static (string Table, string Generation, string CreatedAt, string Id) ResolveNames(
        ExploreDbContext dbContext)
    {
        IEntityType entityType = dbContext.Model.FindEntityType(
            typeof(InstanceBootstrapState))
            ?? throw new InvalidOperationException(
                "Instance bootstrap state is not mapped.");
        string tableName = entityType.GetTableName()
            ?? throw new InvalidOperationException(
                "Instance bootstrap state has no table mapping.");
        var storeObject = StoreObjectIdentifier.Table(
            tableName,
            entityType.GetSchema());
        ISqlGenerationHelper sql = dbContext.GetService<ISqlGenerationHelper>();
        return (
            sql.DelimitIdentifier(tableName, entityType.GetSchema()),
            ResolveColumn(
                entityType,
                storeObject,
                state => state.Generation,
                sql),
            ResolveColumn(
                entityType,
                storeObject,
                state => state.CreatedAt,
                sql),
            ResolveColumn(
                entityType,
                storeObject,
                state => state.Id,
                sql));
    }

    private static string ResolveColumn(
        IEntityType entityType,
        StoreObjectIdentifier storeObject,
        Expression<Func<InstanceBootstrapState, object>> expression,
        ISqlGenerationHelper sql)
    {
        Expression body = expression.Body is UnaryExpression unary
            ? unary.Operand
            : expression.Body;
        if (body is not MemberExpression member
            || member.Expression != expression.Parameters[0])
        {
            throw new ArgumentException(
                "Bootstrap locks require a direct mapped property expression.",
                nameof(expression));
        }

        IProperty property = entityType.FindProperty(member.Member.Name)
            ?? throw new InvalidOperationException(
                $"Bootstrap property '{member.Member.Name}' is not mapped.");
        return sql.DelimitIdentifier(
            property.GetColumnName(storeObject)
            ?? throw new InvalidOperationException(
                $"Bootstrap property '{member.Member.Name}' has no column mapping."));
    }
}
