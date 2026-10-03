using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Schema;

internal static class StorageSourceUriCutoverGuard
{
    private const string ConstraintName = "ck_storage_source_no_unclassified_legacy_locator";

    public static IReadOnlyList<MigrationOperation> Prepare(
        IReadOnlyList<MigrationOperation> operations,
        ISqlGenerationHelper sql,
        bool sqlite,
        string byteLengthFunction)
    {
        var cutovers = operations
            .Where(operation => operation switch
            {
                RenameColumnOperation rename => rename.Name == "uri" && rename.NewName == "source_uri"
                    && rename.Table.EndsWith("storage_objects", StringComparison.Ordinal),
                DropColumnOperation drop => drop.Name == "uri"
                    && drop.Table.EndsWith("storage_objects", StringComparison.Ordinal),
                _ => false
            })
            .ToList();
        if (cutovers.Count == 0)
            return operations;

        var prepared = new List<MigrationOperation>(operations.Count + cutovers.Count * 3);
        foreach (var operation in operations)
        {
            if (!cutovers.Contains(operation))
            {
                prepared.Add(operation);
                continue;
            }

            var (tableName, schema) = operation is RenameColumnOperation rename
                ? (rename.Table, rename.Schema)
                : (((DropColumnOperation)operation).Table, ((DropColumnOperation)operation).Schema);
            string column = sql.DelimitIdentifier("uri");
            string table = sql.DelimitIdentifier(tableName, schema);
            string condition = $"{column} IS NULL OR {EmptyValue(column, sqlite, byteLengthFunction)}";
            prepared.Add(sqlite
                ? new SqlOperation
                {
                    // Existing rows are checked before cutover. Native model rebuild drops the guard column.
                    Sql = $"ALTER TABLE {table} ADD COLUMN {sql.DelimitIdentifier("__storage_source_cutover_guard")} "
                        + $"INTEGER CONSTRAINT {sql.DelimitIdentifier(ConstraintName)} CHECK ({condition});"
                }
                : new AddCheckConstraintOperation
                {
                    Name = ConstraintName, Table = tableName, Schema = schema, Sql = condition
                });
            // Drop cannot remove a column while its validating constraint still depends on it.
            if (!sqlite)
                prepared.Add(new DropCheckConstraintOperation
                {
                    Name = ConstraintName, Table = tableName, Schema = schema
                });
            prepared.Add(operation);
        }

        // Run after the native nullable-column alteration/rebuild; an empty old locator is absence, not origin.
        foreach (var rename in cutovers.OfType<RenameColumnOperation>())
        {
            string table = sql.DelimitIdentifier(rename.Table, rename.Schema);
            string column = sql.DelimitIdentifier(rename.NewName);
            prepared.Add(new SqlOperation
            {
                Sql = $"UPDATE {table} SET {column} = NULL WHERE {EmptyValue(column, sqlite, byteLengthFunction)};"
            });
        }
        return prepared;
    }

    private static string EmptyValue(string column, bool sqlite, string byteLengthFunction) =>
        $"{byteLengthFunction}({(sqlite ? $"CAST({column} AS BLOB)" : column)}) = 0";
}
