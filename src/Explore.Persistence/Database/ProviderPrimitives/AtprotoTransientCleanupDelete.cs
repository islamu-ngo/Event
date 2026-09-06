// ABOUTME: Deletes a fixed batch of expired ATProto lifecycle identities without provider retries.
// ABOUTME: Keeps cleanup within its destructive-command budget even when a committed acknowledgement is lost.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Database;

internal static class AtprotoTransientCleanupDelete
{
    internal static Task<int> ExecuteAsync<TEntity>(ExploreDbContext context, Guid[] ids, CancellationToken cancellationToken)
        where TEntity : class
    {
        if (ids.Length == 0) return Task.FromResult(0);

        IEntityType entity = context.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException("The ATProto cleanup relational mapping is unavailable.");
        string tableName = entity.GetTableName()
            ?? throw new InvalidOperationException("The ATProto cleanup table mapping is unavailable.");
        var table = StoreObjectIdentifier.Table(tableName, entity.GetSchema());
        string idColumn = entity.FindPrimaryKey()?.Properties.Single().GetColumnName(table)
            ?? throw new InvalidOperationException("The ATProto cleanup identity mapping is unavailable.");
        ISqlGenerationHelper sql = context.GetService<ISqlGenerationHelper>();
        string placeholders = string.Join(", ", Enumerable.Range(0, ids.Length).Select(index => $"{{{index}}}"));
        string command = $"DELETE FROM {sql.DelimitIdentifier(tableName, entity.GetSchema())} "
            + $"WHERE {sql.DelimitIdentifier(idColumn)} IN ({placeholders})";

        // ExecuteSqlRawAsync does not enter the provider retry strategy. A lost
        // acknowledgement fails the sweep; the next scheduled pass resumes it.
        return context.Database.ExecuteSqlRawAsync(command, ids.Cast<object>(), cancellationToken);
    }
}
