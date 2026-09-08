using System.Linq.Expressions;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Federation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class AtprotoJetstreamCommitFence
{
    public static async Task<bool> IsCurrentAsync(
        ExploreDbContext dbContext,
        AtprotoJetstreamClaim claim,
        CancellationToken cancellationToken)
    {
        if (RelationalProviderClassifier.Classify(dbContext.Database) != RelationalProvider.PostgreSql)
        {
            DateTime now = DateTime.UtcNow;
            return await dbContext.AtprotoJetstreamConsumerStates.AnyAsync(value =>
                value.Id == claim.ConsumerStateId
                && value.Service == claim.Service
                && value.LeaseToken == claim.LeaseToken
                && value.LeaseFence == claim.LeaseFence
                && value.LeaseExpiresAt > now,
                cancellationToken);
        }

        IEntityType entityType = dbContext.Model.FindEntityType(typeof(AtprotoJetstreamConsumerState))
            ?? throw new InvalidOperationException("Jetstream consumer state is not mapped.");
        string tableName = entityType.GetTableName()
            ?? throw new InvalidOperationException("Jetstream consumer state has no table mapping.");
        var storeObject = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());
        ISqlGenerationHelper sql = dbContext.GetService<ISqlGenerationHelper>();
        string table = sql.DelimitIdentifier(tableName, entityType.GetSchema());
        string id = ResolveColumn(dbContext, storeObject, value => value.Id);
        string service = ResolveColumn(dbContext, storeObject, value => value.Service);
        string leaseToken = ResolveColumn(dbContext, storeObject, value => value.LeaseToken);
        string leaseFence = ResolveColumn(dbContext, storeObject, value => value.LeaseFence);
        string leaseExpiresAt = ResolveColumn(dbContext, storeObject, value => value.LeaseExpiresAt);
        string command = $"UPDATE {table} SET {service} = {service} " +
            $"WHERE {id} = {{0}} AND {service} = {{1}} AND {leaseToken} = {{2}} " +
            $"AND {leaseFence} = {{3}} AND {leaseExpiresAt} > clock_timestamp()";
        int affected = await dbContext.Database.ExecuteSqlRawAsync(
            command,
            [claim.ConsumerStateId, claim.Service, claim.LeaseToken, claim.LeaseFence],
            cancellationToken);
        return affected == 1;
    }

    private static string ResolveColumn(
        ExploreDbContext dbContext,
        StoreObjectIdentifier storeObject,
        Expression<Func<AtprotoJetstreamConsumerState, object>> expression)
    {
        Expression body = expression.Body is UnaryExpression unary
            ? unary.Operand
            : expression.Body;
        if (body is not MemberExpression member)
        {
            throw new ArgumentException(
                "Jetstream fences require direct mapped property expressions.",
                nameof(expression));
        }

        IEntityType entityType = dbContext.Model.FindEntityType(typeof(AtprotoJetstreamConsumerState))!;
        IProperty property = entityType.FindProperty(member.Member.Name)
            ?? throw new InvalidOperationException(
                $"Jetstream property '{member.Member.Name}' is not mapped.");
        ISqlGenerationHelper sql = dbContext.GetService<ISqlGenerationHelper>();
        return sql.DelimitIdentifier(
            property.GetColumnName(storeObject)
            ?? throw new InvalidOperationException(
                $"Jetstream property '{member.Member.Name}' has no column mapping."));
    }
}
