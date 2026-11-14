using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Schema;

/// <summary>Preserves legacy targets and refuses downgrades that cannot represent retained authentication state.</summary>
internal static class AtprotoFlowKindMigrationOperations
{
    private const string DowngradeConstraint = "ck_atproto_flow_kind_downgrade_targeted";

    internal static IReadOnlyList<MigrationOperation> Prepare(
        IReadOnlyList<MigrationOperation> operations, ISqlGenerationHelper sql, bool sqlite)
    {
        var removals = operations.OfType<DropColumnOperation>().Where(column =>
            IsFlowKind(column.Name, column.Table)).ToArray();
        if (removals.Length == 0
            && !operations.OfType<AddColumnOperation>().Any(column => IsFlowKind(column.Name, column.Table)))
            return operations;
        List<MigrationOperation> prepared = [];
        foreach (var removal in removals)
        {
            string purpose = sql.DelimitIdentifier("purpose");
            string tenant = sql.DelimitIdentifier("tenant_id");
            string kind = sql.DelimitIdentifier(removal.Name);
            string condition = $"({purpose} = 3 AND {tenant} IS NULL AND {kind} IS NULL)"
                + $" OR ({purpose} IN (1, 2) AND {tenant} IS NOT NULL AND {kind} IS NOT NULL AND {kind} = 1)";
            prepared.Add(sqlite
                ? new SqlOperation
                {
                    Sql = $"ALTER TABLE {sql.DelimitIdentifier(removal.Table, removal.Schema)}"
                        + $" ADD COLUMN {sql.DelimitIdentifier("__atproto_flow_kind_downgrade_guard")} INTEGER"
                        + $" CONSTRAINT {sql.DelimitIdentifier(DowngradeConstraint)} CHECK ({condition});"
                }
                : new AddCheckConstraintOperation
                {
                    Name = DowngradeConstraint,
                    Table = removal.Table,
                    Schema = removal.Schema,
                    Sql = condition
                });
        }
        foreach (var operation in operations)
        {
            if (operation is DropColumnOperation removal && IsFlowKind(removal.Name, removal.Table) && !sqlite)
                prepared.Add(new DropCheckConstraintOperation
                {
                    Name = DowngradeConstraint,
                    Table = removal.Table,
                    Schema = removal.Schema
                });
            if (operation is not AddColumnOperation column || !IsFlowKind(column.Name, column.Table))
            {
                prepared.Add(operation);
                continue;
            }
            string table = sql.DelimitIdentifier(column.Table, column.Schema);
            string kind = sql.DelimitIdentifier(column.Name);
            if (sqlite)
            {
                // SQLite defers constraint-driven table rebuilds. Materialize this
                // column first so their copy reads classified rows, not a NULL default.
                prepared.Add(new SqlOperation { Sql = $"ALTER TABLE {table} ADD COLUMN {kind} INTEGER NULL;" });
            }
            else
            {
                prepared.Add(column);
            }
            prepared.Add(new SqlOperation
            {
                Sql = $"UPDATE {table} SET {kind} = 1"
                    + $" WHERE {sql.DelimitIdentifier("purpose")} IN (1, 2)"
                    + $" AND {sql.DelimitIdentifier("tenant_id")} IS NOT NULL AND {kind} IS NULL;"
            });
        }
        return prepared;
    }

    private static bool IsFlowKind(string name, string table) =>
        name == "flow_kind"
        && table.EndsWith("atproto_transient_records", StringComparison.Ordinal);
}
