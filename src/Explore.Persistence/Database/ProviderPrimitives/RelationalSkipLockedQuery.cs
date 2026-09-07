using System.Linq.Expressions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class RelationalSkipLockedQuery
{
    public static bool IsSupported(ExploreDbContext dbContext) =>
        RelationalProviderClassifier.Classify(dbContext.Database) == RelationalProvider.PostgreSql;

    public static Task<WebhookBulkReplayOperation?> LoadNextWebhookBulkReplayAsync(
        ExploreDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!IsSupported(dbContext))
        {
            return dbContext.WebhookBulkReplayOperations
                .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookWorkerCrossTenantQueue)
                .Where(operation =>
                    operation.StatusId == (int)WebhookBulkReplayStatus.Queued)
                .OrderBy(operation => operation.QueuedAt)
                .ThenBy(operation => operation.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        (string table, string status, string queuedAt, string id) =
            ResolveQueueNames<WebhookBulkReplayOperation>(
                dbContext,
                operation => operation.StatusId,
                operation => operation.QueuedAt,
                operation => operation.Id);
        string sql = $"SELECT * FROM {table} WHERE {status} = {{0}} " +
            $"ORDER BY {queuedAt}, {id} FOR UPDATE SKIP LOCKED LIMIT 1";
        return dbContext.WebhookBulkReplayOperations
            .FromSqlRaw(sql, (int)WebhookBulkReplayStatus.Queued)
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookWorkerCrossTenantQueue)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public static Task<List<EmailDispatchOutbox>> LoadStaleEmailDispatchesAsync(
        ExploreDbContext dbContext,
        DateTime processingStartedBefore,
        int batchSize,
        CancellationToken cancellationToken)
    {
        (string table, string status, string processingStartedAt, string id) =
            ResolveQueueNames<EmailDispatchOutbox>(
                dbContext,
                outbox => outbox.Status,
                outbox => outbox.ProcessingStartedAt,
                outbox => outbox.Id);
        string sql = $"SELECT * FROM {table} WHERE {status} = {{0}} " +
            $"AND {processingStartedAt} IS NOT NULL AND {processingStartedAt} <= {{1}} " +
            $"ORDER BY {processingStartedAt}, {id} FOR UPDATE SKIP LOCKED LIMIT {{2}}";
        return dbContext.EmailDispatchOutbox
            .FromSqlRaw(
                sql,
                (int)EmailDispatchStatus.Processing,
                processingStartedBefore,
                batchSize)
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDispatchWorkerCrossTenantQueue)
            .ToListAsync(cancellationToken);
    }

    private static (string Table, string Status, string Order, string Id)
        ResolveQueueNames<TEntity>(
            ExploreDbContext dbContext,
            Expression<Func<TEntity, object>> statusProperty,
            Expression<Func<TEntity, object>> orderProperty,
            Expression<Func<TEntity, object>> idProperty)
        where TEntity : class
    {
        IEntityType entityType = dbContext.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException(
                $"Queue entity '{typeof(TEntity).Name}' is not mapped.");
        string tableName = entityType.GetTableName()
            ?? throw new InvalidOperationException(
                $"Queue entity '{typeof(TEntity).Name}' has no table mapping.");
        var storeObject = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());
        ISqlGenerationHelper sql = dbContext.GetService<ISqlGenerationHelper>();
        return (
            sql.DelimitIdentifier(tableName, entityType.GetSchema()),
            ResolveColumn(entityType, storeObject, statusProperty, sql),
            ResolveColumn(entityType, storeObject, orderProperty, sql),
            ResolveColumn(entityType, storeObject, idProperty, sql));
    }

    private static string ResolveColumn<TEntity>(
        IEntityType entityType,
        StoreObjectIdentifier storeObject,
        Expression<Func<TEntity, object>> expression,
        ISqlGenerationHelper sql)
    {
        Expression body = expression.Body is UnaryExpression unary
            ? unary.Operand
            : expression.Body;
        if (body is not MemberExpression member ||
            member.Expression != expression.Parameters[0])
        {
            throw new ArgumentException(
                "Queue locks require a direct mapped property expression.",
                nameof(expression));
        }

        IProperty property = entityType.FindProperty(member.Member.Name)
            ?? throw new InvalidOperationException(
                $"Queue property '{member.Member.Name}' is not mapped.");
        return sql.DelimitIdentifier(
            property.GetColumnName(storeObject)
            ?? throw new InvalidOperationException(
                $"Queue property '{member.Member.Name}' has no column mapping."));
    }
}
