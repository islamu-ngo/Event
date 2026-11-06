using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Schema;

/// <summary>
/// Refuses removal of visitor evidence and prevents EF's nullable-column downgrade
/// defaults from fabricating administrator provenance.
/// </summary>
internal sealed class LocalVisitorDowngradeGuard : IDisposable
{
    private const string ProvenanceColumn = "visitor_enrollment_operation_id";
    private const string NativeConstraint = "ck_local_visitor_downgrade_original_authority";
    private const string ReceiptConstraint = "ck_local_visitor_downgrade_empty_receipts";
    private readonly List<(AlterColumnOperation Operation, object? Value, string? Sql)> defaults = [];

    private LocalVisitorDowngradeGuard(IReadOnlyList<MigrationOperation> operations)
    {
        Operations = operations;
    }

    internal IReadOnlyList<MigrationOperation> Operations { get; private set; }
    internal IReadOnlyList<MigrationOperation> PreflightOperations { get; private set; } = [];

    internal static LocalVisitorDowngradeGuard Prepare(
        IReadOnlyList<MigrationOperation> operations, ISqlGenerationHelper sql, bool sqlite)
    {
        var result = new LocalVisitorDowngradeGuard(operations);
        var receipts = operations.OfType<DropTableOperation>()
            .Where(operation => operation.Name is "visitor_enrollment_operations" or "ie_visitor_enrollment_operations")
            .ToArray();
        var native = operations.OfType<DropColumnOperation>()
            .Where(operation => IsNative(operation.Table) && operation.Name == ProvenanceColumn).ToArray();
        if (receipts.Length == 0 && native.Length == 0) return result;

        List<MigrationOperation> preflight = [];
        foreach (var receipt in receipts)
            preflight.Add(Guard(receipt.Name, receipt.Schema, ReceiptConstraint,
                $"{sql.DelimitIdentifier("id")} IS NULL", sql, sqlite));
        foreach (var source in native)
        {
            string condition = $"{sql.DelimitIdentifier("kind")} IN (1, 2)"
                + $" AND {sql.DelimitIdentifier(ProvenanceColumn)} IS NULL"
                + $" AND {sql.DelimitIdentifier("initiating_application_user_id")} IS NOT NULL"
                + $" AND {sql.DelimitIdentifier("verified_by_application_user_id")} IS NOT NULL"
                + $" AND {sql.DelimitIdentifier("verified_at")} IS NOT NULL";
            preflight.Add(Guard(source.Table, source.Schema, NativeConstraint, condition, sql, sqlite));
        }

        List<MigrationOperation> body = [];
        foreach (var operation in operations)
        {
            if (operation is AlterColumnOperation alter && IsNative(alter.Table)
                && !alter.IsNullable && alter.OldColumn.IsNullable
                && alter.Name is "initiating_application_user_id" or "verified_by_application_user_id" or "verified_at")
            {
                result.defaults.Add((alter, alter.DefaultValue, alter.DefaultValueSql));
                alter.DefaultValue = null;
                alter.DefaultValueSql = null;
            }
            if (!sqlite && operation is DropColumnOperation drop
                && IsNative(drop.Table) && drop.Name == ProvenanceColumn)
                body.Add(new DropCheckConstraintOperation
                { Name = NativeConstraint, Table = drop.Table, Schema = drop.Schema });
            body.Add(operation);
        }
        result.Operations = body;
        result.PreflightOperations = preflight;
        return result;
    }

    private static MigrationOperation Guard(string table, string? schema, string constraint,
        string condition, ISqlGenerationHelper sql, bool sqlite) =>
        sqlite
            ? new SqlOperation
            {
                Sql = $"ALTER TABLE {sql.DelimitIdentifier(table, schema)}"
                    + $" ADD COLUMN {sql.DelimitIdentifier("__local_visitor_downgrade_guard")} INTEGER"
                    + $" CONSTRAINT {sql.DelimitIdentifier(constraint)} CHECK ({condition});"
            }
            : new AddCheckConstraintOperation
            { Name = constraint, Table = table, Schema = schema, Sql = condition };

    private static bool IsNative(string table) =>
        table is "local_identity_credential_operation" or "ie_local_identity_credential_operation";

    public void Dispose()
    {
        foreach (var entry in defaults)
        {
            entry.Operation.DefaultValue = entry.Value;
            entry.Operation.DefaultValueSql = entry.Sql;
        }
    }
}
