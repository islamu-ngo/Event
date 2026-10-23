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

    public static async Task<WebhookBulkReplayOperation?> LoadNextWebhookBulkReplayAsync(
        ExploreDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (dbContext.TenantFilterTenantId is not { } tenantId || tenantId == Guid.Empty
            || dbContext.IsTenantFilterBypassed || !dbContext.Database.IsRelational()
            || dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Webhook replay acquisition requires an exact tenant lease and native transaction.");
        await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(dbContext, tenantId, cancellationToken);
        if (!await dbContext.Tenants.AsNoTracking().AnyAsync(
                tenant => tenant.Id == tenantId && tenant.TenantStatusId == (int)TenantStatusEnum.Active,
                cancellationToken))
            return null;

        IQueryable<WebhookBulkReplayOperation> query;
        if (!IsSupported(dbContext))
        {
            query = dbContext.WebhookBulkReplayOperations.AsNoTracking()
                .Where(operation =>
                    operation.TenantId == tenantId && operation.StatusId == (int)WebhookBulkReplayStatus.Queued)
                .OrderBy(operation => operation.QueuedAt)
                .ThenBy(operation => operation.Id)
                .Take(1);
        }
        else
        {
            (string table, string status, string queuedAt, string id, string tenant) =
                ResolveQueueNames<WebhookBulkReplayOperation>(
                    dbContext,
                    operation => operation.StatusId,
                    operation => operation.QueuedAt,
                    operation => operation.Id,
                    operation => operation.TenantId);
            string sql = $"SELECT * FROM {table} WHERE {tenant} = {{0}} AND {status} = {{1}} " +
                $"ORDER BY {queuedAt}, {id} FOR UPDATE SKIP LOCKED LIMIT 1";
            query = dbContext.WebhookBulkReplayOperations
                .FromSqlRaw(sql, tenantId, (int)WebhookBulkReplayStatus.Queued).AsNoTracking();
        }
        var selected = await query.SingleOrDefaultAsync(cancellationToken);
        if (selected is not null)
        {
            if (dbContext.WebhookBulkReplayOperations.Local.Any(row => row.Id == selected.Id))
                throw new InvalidOperationException("Webhook replay acquisition cannot reuse a supplied tracked operation.");
            dbContext.Entry(selected).State = EntityState.Unchanged;
        }
        return selected;
    }

    public static Task<List<EmailDispatchOutbox>> LoadStaleEmailDispatchesAsync(
        ExploreDbContext dbContext,
        DateTime processingStartedBefore,
        int batchSize,
        CancellationToken cancellationToken)
    {
        (string table, string status, string processingStartedAt, string id, _) =
            ResolveQueueNames<EmailDispatchOutbox>(
                dbContext,
                outbox => outbox.Status,
                outbox => outbox.ProcessingStartedAt,
                outbox => outbox.Id,
                outbox => outbox.TenantId);
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

    private static (string Table, string Status, string Order, string Id, string Tenant)
        ResolveQueueNames<TEntity>(
            ExploreDbContext dbContext,
            Expression<Func<TEntity, object?>> statusProperty,
            Expression<Func<TEntity, object?>> orderProperty,
            Expression<Func<TEntity, object?>> idProperty,
            Expression<Func<TEntity, object?>> tenantProperty)
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
            ResolveColumn(entityType, storeObject, idProperty, sql),
            ResolveColumn(entityType, storeObject, tenantProperty, sql));
    }

    private static string ResolveColumn<TEntity>(
        IEntityType entityType,
        StoreObjectIdentifier storeObject,
        Expression<Func<TEntity, object?>> expression,
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
