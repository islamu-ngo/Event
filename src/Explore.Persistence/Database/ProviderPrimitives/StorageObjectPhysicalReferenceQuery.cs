using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class StorageObjectPhysicalReferenceQuery
{
    internal static async Task<bool> HasBlockingReferencesAsync(
        ExploreDbContext dbContext,
        Guid storageObjectId,
        CancellationToken cancellationToken)
    {
        var storage = dbContext.Model.FindEntityType(typeof(StorageObject))!;
        var id = storage.FindProperty(nameof(StorageObject.Id))!;
        var sql = dbContext.GetService<ISqlGenerationHelper>();
        var predicates = new List<string>();

        // Query physical owner relations, not a second usage inventory or filtered
        // entity projections. A composite FK contributes every column to its join.
        // Custody is transferred by retirement, not counted as a readable owner.
        foreach (var foreignKey in storage.GetReferencingForeignKeys()
            .Where(key => key.DeclaringEntityType.ClrType != typeof(StorageUploadSession)
                && key.DeclaringEntityType.ClrType != typeof(StorageProducerOperation)))
        {
            var constraints = foreignKey.GetMappedConstraints().ToArray();
            if (constraints.Length == 0)
                throw new InvalidOperationException("A storage reference has no relational constraint mapping.");

            foreach (var constraint in constraints)
            {
                string idColumn = id.GetColumnName(StoreObjectIdentifier.Table(
                    constraint.PrincipalTable.Name, constraint.PrincipalTable.Schema))
                    ?? throw new InvalidOperationException("A storage reference has no mapped object identity.");
                string join = string.Join(" AND ", constraint.Columns.Select((column, index) =>
                    $"owner.{sql.DelimitIdentifier(column.Name)} = source.{sql.DelimitIdentifier(constraint.PrincipalColumns[index].Name)}"));
                predicates.Add(
                    $"EXISTS (SELECT 1 FROM {sql.DelimitIdentifier(constraint.Table.Name, constraint.Table.Schema)} owner " +
                    $"INNER JOIN {sql.DelimitIdentifier(constraint.PrincipalTable.Name, constraint.PrincipalTable.Schema)} source ON {join} " +
                    $"WHERE source.{sql.DelimitIdentifier(idColumn)} = {{0}})");
            }
        }

        if (predicates.Count == 0)
            return false;

        // Only model-owned, provider-delimited identifiers enter SQL. The object
        // identity is parameterized; no owner row or cross-tenant name is returned.
        string query = $"SELECT CASE WHEN {string.Join(" OR ", predicates)} THEN 1 ELSE 0 END AS {sql.DelimitIdentifier("Value")}";
        return await dbContext.Database.SqlQueryRaw<int>(query, storageObjectId)
            .SingleAsync(cancellationToken) == 1;
    }
}
