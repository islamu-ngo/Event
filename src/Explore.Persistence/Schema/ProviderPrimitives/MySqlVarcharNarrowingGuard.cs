using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Schema;

/// <summary>
/// Validates every VARCHAR reduction before maintenance DDL starts. Writers must remain
/// quiesced until migration settlement; MySQL requires dropping these checks before MODIFY.
/// </summary>
internal static class MySqlVarcharNarrowingGuard
{
    private const string EnforcementAnnotation = "Explore:VarcharNarrowingPreflight";

    public static bool IsPreflight(AddCheckConstraintOperation operation) =>
        operation[EnforcementAnnotation] is true;

    public static IReadOnlyList<MigrationOperation> Prepare(
        IReadOnlyList<MigrationOperation> operations,
        ISqlGenerationHelper sql)
    {
        foreach (AlterColumnOperation operation in operations.OfType<AlterColumnOperation>())
        {
            bool newVarchar = TryVarcharLength(operation.ColumnType, out int newStoreLength);
            bool oldVarchar = TryVarcharLength(operation.OldColumn.ColumnType, out int oldStoreLength);
            int? newLength = newVarchar ? newStoreLength : operation.MaxLength;
            int? oldLength = oldVarchar ? oldStoreLength : operation.OldColumn.MaxLength;
            bool inferredStringType = operation.ClrType == typeof(string)
                && operation.OldColumn.ClrType == typeof(string)
                && (string.IsNullOrWhiteSpace(operation.ColumnType)
                    || string.IsNullOrWhiteSpace(operation.OldColumn.ColumnType));
            if (newLength is > 0 && oldLength > newLength
                && (newVarchar && oldVarchar || inferredStringType)
                && !IsVarcharNarrowing(operation))
                throw new InvalidOperationException("varchar_narrowing_metadata_not_supported");
        }

        AlterColumnOperation[] narrowings = operations.OfType<AlterColumnOperation>()
            .Where(IsVarcharNarrowing).ToArray();
        if (narrowings.Length == 0)
            return operations;

        foreach (AlterColumnOperation narrowing in narrowings)
        {
            if (narrowing.Collation != narrowing.OldColumn.Collation
                || !Equals(narrowing["MySql:CharSet"], narrowing.OldColumn["MySql:CharSet"])
                || narrowing.IsUnicode != narrowing.OldColumn.IsUnicode
                || narrowing.IsFixedLength != narrowing.OldColumn.IsFixedLength
                || narrowing.IsNullable != narrowing.OldColumn.IsNullable
                || narrowing.ComputedColumnSql != narrowing.OldColumn.ComputedColumnSql
                || narrowing.IsStored != narrowing.OldColumn.IsStored
                || narrowing.IsRowVersion != narrowing.OldColumn.IsRowVersion)
                throw new InvalidOperationException("varchar_narrowing_conversion_not_supported");

            if (operations.Any(operation => ChangesPreflightSource(operation, narrowing)))
                throw new InvalidOperationException("varchar_narrowing_preflight_dependency_not_supported");
        }

        var guarded = new List<MigrationOperation>(operations.Count + narrowings.Length * 2);
        foreach (AlterColumnOperation narrowing in narrowings)
        {
            string limit = narrowing.MaxLength!.Value.ToString(CultureInfo.InvariantCulture);
            string identity = string.Join('\0', narrowing.Schema ?? string.Empty,
                narrowing.Table, narrowing.Name, limit);
            string name = "ck_varchar_narrowing_"
                + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..32];
            var check = new AddCheckConstraintOperation
            {
                Name = name,
                Table = narrowing.Table,
                Schema = narrowing.Schema,
                Sql = $"CHAR_LENGTH({sql.DelimitIdentifier(narrowing.Name)}) <= {limit}"
            };
            check[EnforcementAnnotation] = true;
            guarded.Add(check);
            guarded.Add(new DropCheckConstraintOperation
            {
                Name = name,
                Table = narrowing.Table,
                Schema = narrowing.Schema
            });
        }

        guarded.AddRange(operations);
        return guarded;
    }

    private static bool TryVarcharLength(string? storeType, out int length)
    {
        length = 0;
        ReadOnlySpan<char> type = storeType.AsSpan().Trim();
        if (!type.StartsWith("varchar", StringComparison.OrdinalIgnoreCase))
            return false;
        type = type[7..].Trim();
        return type.Length > 2 && type[0] == '(' && type[^1] == ')'
            && int.TryParse(type[1..^1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out length)
            && length > 0;
    }

    private static bool IsVarcharNarrowing(AlterColumnOperation operation) =>
        operation.ClrType == typeof(string)
        && operation.OldColumn.ClrType == typeof(string)
        && operation.MaxLength is > 0
        && operation.OldColumn.MaxLength > operation.MaxLength
        && StringComparer.OrdinalIgnoreCase.Equals(operation.ColumnType,
            $"varchar({operation.MaxLength.Value.ToString(CultureInfo.InvariantCulture)})")
        && StringComparer.OrdinalIgnoreCase.Equals(operation.OldColumn.ColumnType,
            $"varchar({operation.OldColumn.MaxLength!.Value.ToString(CultureInfo.InvariantCulture)})");

    private static bool ChangesPreflightSource(MigrationOperation operation,
        AlterColumnOperation narrowing) => operation switch
        {
            SqlOperation => true,
            CreateTableOperation create => SameTable(create.Schema, create.Name, narrowing),
            DropTableOperation drop => SameTable(drop.Schema, drop.Name, narrowing),
            RenameTableOperation rename => SameTable(rename.Schema, rename.Name, narrowing)
                || SameTable(rename.NewSchema ?? rename.Schema,
                    rename.NewName ?? rename.Name, narrowing),
            AlterTableOperation alter => SameTable(alter.Schema, alter.Name, narrowing),
            AddColumnOperation add => SameColumn(add.Schema, add.Table, add.Name, narrowing),
            DropColumnOperation drop => SameColumn(drop.Schema, drop.Table, drop.Name, narrowing),
            RenameColumnOperation rename => SameColumn(rename.Schema, rename.Table, rename.Name, narrowing)
                || SameColumn(rename.Schema, rename.Table, rename.NewName, narrowing),
            AlterColumnOperation alter => !ReferenceEquals(alter, narrowing)
                && SameColumn(alter.Schema, alter.Table, alter.Name, narrowing),
            InsertDataOperation insert => SameTable(insert.Schema, insert.Table, narrowing),
            UpdateDataOperation update => SameTable(update.Schema, update.Table, narrowing),
            _ => false
        };

    private static bool SameTable(string? schema, string table, AlterColumnOperation narrowing) =>
        StringComparer.OrdinalIgnoreCase.Equals(schema ?? string.Empty, narrowing.Schema ?? string.Empty)
        && StringComparer.OrdinalIgnoreCase.Equals(table, narrowing.Table);

    private static bool SameColumn(string? schema, string table, string column,
        AlterColumnOperation narrowing) => SameTable(schema, table, narrowing)
        && StringComparer.OrdinalIgnoreCase.Equals(column, narrowing.Name);
}
