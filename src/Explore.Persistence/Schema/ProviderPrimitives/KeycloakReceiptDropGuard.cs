using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Schema;

internal static class KeycloakReceiptDropGuard
{
    private const string ConstraintName =
        "ck_keycloak_receipts_no_unresolved_downgrade";
    private const string GuardSql =
        "state NOT IN ('Applying', 'OutcomeUnknown')";

    public static IReadOnlyList<MigrationOperation> Prepare(
        IReadOnlyList<MigrationOperation> operations,
        ISqlGenerationHelper sqlGenerationHelper,
        bool sqlite)
    {
        if (!operations.OfType<DropTableOperation>()
            .Any(operation => IsReceiptTable(operation.Name)))
        {
            return operations;
        }

        var prepared = new List<MigrationOperation>(
            operations.Count + 1);
        foreach (MigrationOperation operation in operations)
        {
            if (operation is DropTableOperation drop
                && IsReceiptTable(drop.Name))
            {
                prepared.Add(sqlite
                    ? CreateSqliteGuard(drop, sqlGenerationHelper)
                    : CreateRelationalGuard(drop));
            }

            prepared.Add(operation);
        }

        return prepared;
    }

    private static AddCheckConstraintOperation CreateRelationalGuard(
        DropTableOperation drop) =>
        new()
        {
            Name = ConstraintName,
            Table = drop.Name,
            Schema = drop.Schema,
            Sql = GuardSql
        };

    private static SqlOperation CreateSqliteGuard(
        DropTableOperation drop,
        ISqlGenerationHelper sqlGenerationHelper)
    {
        string table = sqlGenerationHelper.DelimitIdentifier(
            drop.Name,
            drop.Schema);
        string column = sqlGenerationHelper.DelimitIdentifier(
            "__keycloak_receipt_downgrade_guard");
        string constraint = sqlGenerationHelper.DelimitIdentifier(
            ConstraintName);
        string state = sqlGenerationHelper.DelimitIdentifier("state");

        return new SqlOperation
        {
            Sql = $"""
                   ALTER TABLE {table}
                   ADD COLUMN {column} INTEGER
                   CONSTRAINT {constraint}
                   CHECK ({state} NOT IN ('Applying', 'OutcomeUnknown'));
                   """,
            SuppressTransaction = false
        };
    }

    private static bool IsReceiptTable(string table) =>
        table is "KeycloakOperationReceipts"
            or "ie_KeycloakOperationReceipts";
}
