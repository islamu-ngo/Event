using System.Globalization;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Schema;

/// <summary>Preserves the two merged resource histories without rewriting generated migrations.</summary>
/// <remarks>
/// Only the source-owner Up rebuild uses the native merged EF model, and only for event_resources.
/// Down uses its historical target so intentionally removed fields stay removed. SQLite cannot
/// conditionally copy a column absent from one branch: an integer-only schema preflight rejects
/// unexpected source columns before owner-table DDL or FK-state changes. This storage primitive
/// reads pragma_table_xinfo, not business rows, and requires an exclusive migration window.
/// </remarks>
internal static class SqliteMergedResourceHistory
{
    private const string RelativeOrder = "ck_event_resources_relative_availability_order";
    private static readonly string[] NativeColumns = ["material_version_id", "moderation_quarantine_count"];
    private static readonly string[] NativeChecks = ["ck_event_resources_material", "ck_event_resources_quarantine"];

    internal sealed record Prepared(
        IReadOnlyList<MigrationOperation> OtherOperations,
        IReadOnlyList<MigrationOperation> ResourceOperations,
        IModel? ResourceModel,
        IReadOnlyList<MigrationOperation> PreflightOperations);

    internal static Prepared Prepare(
        IReadOnlyList<MigrationOperation> operations, IModel? target, DbContext context)
    {
        if (!operations.Any(IsHistoryBoundary)) return new(operations, [], target, []);
        if (target is null) throw UnsafeHistory("The exact historical target model is required.");
        var entity = target.FindEntityType(typeof(EventResource).FullName!)
            ?? throw UnsafeHistory("The historical resource entity is missing.");
        string tableName = entity.GetTableName()
            ?? throw UnsafeHistory("The historical resource table is missing.");
        if (entity.GetSchema() is not null) throw UnsafeHistory("SQLite requires a prefixed resource table.");
        var resource = operations.Where(operation => TableName(operation) == tableName).ToArray();
        bool nativeUp = IsNativeUp(resource);
        bool nativeDown = resource.Length == 4
            && NativeColumns.All(name => resource.OfType<DropColumnOperation>().Count(column => column.Name == name && column.Schema is null) == 1)
            && NativeChecks.All(name => resource.OfType<DropCheckConstraintOperation>().Count(check => check.Name == name && check.Schema is null) == 1);
        bool relativeUp = IsRelativeUp(resource);
        bool relativeDown = resource is [DropCheckConstraintOperation { Name: RelativeOrder, Schema: null }];
        if (!(nativeUp || nativeDown || relativeUp || relativeDown)
            || operations.OfType<SqlOperation>().Any(operation => operation.Sql.Contains(tableName, StringComparison.OrdinalIgnoreCase)))
            throw UnsafeHistory("An unrecognized resource transition cannot be rebuilt safely.");

        IModel merged = context.GetService<IDesignTimeModel>().Model;
        var mergedTable = merged.GetRelationalModel().FindTable(tableName, null)
            ?? throw UnsafeHistory("The merged resource table is missing.");
        if (!NativeColumns.All(name => mergedTable.FindColumn(name) is not null)
            || !NativeChecks.Append(RelativeOrder).All(name => mergedTable.CheckConstraints.Any(check => check.Name == name)))
            throw UnsafeHistory("The native EF model must include both resource histories.");
        var difference = context.GetService<IMigrationsModelDiffer>()
            .GetDifferences(target.GetRelationalModel(), merged.GetRelationalModel())
            .Where(operation => TableName(operation) == tableName).ToArray();
        bool expectedDifference = nativeUp
            ? difference.Length == 0 || IsRelativeUp(difference)
            : relativeDown
                ? IsRelativeUp(difference)
                    || difference.Count(operation => operation is AddCheckConstraintOperation { Name: RelativeOrder }) == 1
                        && IsNativeUp(difference.Where(operation => operation is not AddCheckConstraintOperation { Name: RelativeOrder }).ToArray())
                : IsNativeUp(difference) || relativeUp && difference.Length == 0;
        if (!expectedDifference)
            throw UnsafeHistory("The merged resource model differs beyond the two audited history changes.");

        IModel resourceModel = nativeUp ? merged : target;
        var table = resourceModel.GetRelationalModel().FindTable(tableName, null)
            ?? throw UnsafeHistory("The selected resource table is missing.");
        foreach (var check in resource.OfType<AddCheckConstraintOperation>())
            if (!table.CheckConstraints.Any(expected => expected.Name == check.Name && expected.Sql == check.Sql))
                throw UnsafeHistory("The resource constraint does not match its native EF target.");

        string[] sourceColumns = table.Columns.Select(column => column.Name)
            .Except(resource.OfType<AddColumnOperation>().Select(column => column.Name), StringComparer.Ordinal)
            .Concat(resource.OfType<DropColumnOperation>().Select(column => column.Name))
            .Order(StringComparer.Ordinal).ToArray();
        var sql = context.GetService<ISqlGenerationHelper>();
        string guardName = tableName + "_merge_guard";
        var guard = new CreateTableOperation { Name = guardName };
        guard.Columns.Add(new AddColumnOperation { Name = "violation", Table = guardName, ClrType = typeof(int), IsNullable = false });
        guard.CheckConstraints.Add(new AddCheckConstraintOperation
        {
            Name = "ck_sqlite_merged_resource_history", Table = guardName,
            Sql = sql.DelimitIdentifier("violation") + " = 0"
        });
        string columns = string.Join(", ", sourceColumns.Select(Literal));
        var preflight = new SqlOperation
        {
            Sql = $"""
                INSERT INTO {sql.DelimitIdentifier(guardName)} ({sql.DelimitIdentifier("violation")})
                SELECT 1 WHERE (SELECT COUNT(*) FROM pragma_table_xinfo({Literal(tableName)})) <> {sourceColumns.Length.ToString(CultureInfo.InvariantCulture)}
                    OR EXISTS (SELECT 1 FROM pragma_table_xinfo({Literal(tableName)}) WHERE name NOT IN ({columns}));
                """,
            SuppressTransaction = false
        };
        return new(operations.Where(operation => !resource.Contains(operation)).ToArray(), resource,
            resourceModel, [guard, preflight, new DropTableOperation { Name = guardName }]);
    }

    private static bool IsHistoryBoundary(MigrationOperation operation) => operation switch
    {
        AddCheckConstraintOperation check => check.Name == RelativeOrder || NativeChecks.Contains(check.Name, StringComparer.Ordinal),
        DropCheckConstraintOperation check => check.Name == RelativeOrder || NativeChecks.Contains(check.Name, StringComparer.Ordinal),
        ColumnOperation column => column.Table.EndsWith("event_resources", StringComparison.Ordinal)
            && NativeColumns.Contains(column.Name, StringComparer.Ordinal),
        DropColumnOperation column => column.Table.EndsWith("event_resources", StringComparison.Ordinal)
            && NativeColumns.Contains(column.Name, StringComparer.Ordinal),
        _ => false
    };

    private static bool IsRelativeUp(IReadOnlyList<MigrationOperation> operations) =>
        operations is [AddCheckConstraintOperation { Name: RelativeOrder, Schema: null }];

    private static bool IsNativeUp(IReadOnlyList<MigrationOperation> operations) => operations.Count == 4
        && operations.OfType<AddColumnOperation>().Count(column => column is
            { Name: "material_version_id", Schema: null, IsNullable: true, DefaultValue: null, DefaultValueSql: null }
            && column.ClrType == typeof(Guid)) == 1
        && operations.OfType<AddColumnOperation>().Count(column => column is
            { Name: "moderation_quarantine_count", Schema: null, IsNullable: false, DefaultValueSql: null }
            && column.ClrType == typeof(int) && Equals(column.DefaultValue, 0)) == 1
        && NativeChecks.All(name => operations.OfType<AddCheckConstraintOperation>()
            .Count(check => check.Name == name && check.Schema is null) == 1);

    private static string? TableName(MigrationOperation operation) => operation switch
    {
        ColumnOperation column => column.Table,
        DropColumnOperation drop => drop.Table,
        AddCheckConstraintOperation check => check.Table,
        DropCheckConstraintOperation check => check.Table,
        CreateIndexOperation index => index.Table,
        DropIndexOperation index => index.Table,
        RenameIndexOperation index => index.Table,
        AddForeignKeyOperation key => key.Table,
        DropForeignKeyOperation key => key.Table,
        AddPrimaryKeyOperation key => key.Table,
        DropPrimaryKeyOperation key => key.Table,
        AddUniqueConstraintOperation key => key.Table,
        DropUniqueConstraintOperation key => key.Table,
        CreateTableOperation table => table.Name,
        DropTableOperation table => table.Name,
        AlterTableOperation table => table.Name,
        RenameTableOperation table => table.Name,
        RenameColumnOperation column => column.Table,
        _ => null
    };

    private static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    private static InvalidOperationException UnsafeHistory(string reason) => new("Unsafe SQLite merged resource history: " + reason);
}
