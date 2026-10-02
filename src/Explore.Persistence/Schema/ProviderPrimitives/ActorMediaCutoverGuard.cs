using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Schema;

internal static class ActorMediaCutoverGuard
{
    private const string ConstraintName = "ck_actor_media_no_unclassified_legacy_source";

    public static IReadOnlyList<MigrationOperation> Prepare(
        IReadOnlyList<MigrationOperation> operations,
        ISqlGenerationHelper sql,
        bool sqlite)
    {
        if (!operations.OfType<RenameColumnOperation>().Any(IsCutover))
            return operations;

        var prepared = new List<MigrationOperation>(operations.Count + 2);
        foreach (var operation in operations)
        {
            if (operation is not RenameColumnOperation rename || !IsCutover(rename))
            {
                prepared.Add(operation);
                continue;
            }

            string column = sql.DelimitIdentifier(rename.Name);
            prepared.Add(sqlite
                ? new SqlOperation
                {
                    // SQLite validates existing rows; the following model rebuild drops this guard column.
                    Sql = $"ALTER TABLE {sql.DelimitIdentifier(rename.Table, rename.Schema)} "
                        + $"ADD COLUMN {sql.DelimitIdentifier("__actor_media_cutover_guard")} INTEGER "
                        + $"CONSTRAINT {sql.DelimitIdentifier(ConstraintName)} CHECK ({column} IS NULL);"
                }
                : new AddCheckConstraintOperation
                {
                    Name = ConstraintName,
                    Table = rename.Table,
                    Schema = rename.Schema,
                    Sql = $"{column} IS NULL"
                });
            prepared.Add(operation);
            if (!sqlite)
                prepared.Add(new DropCheckConstraintOperation
                {
                    Name = ConstraintName,
                    Table = rename.Table,
                    Schema = rename.Schema
                });
        }
        return prepared;
    }

    private static bool IsCutover(RenameColumnOperation operation) =>
        operation.Name == "profile_picture_uri"
        && operation.NewName == "external_profile_picture_uri"
        && operation.Table.EndsWith("actor_pii", StringComparison.Ordinal);
}
