#nullable enable

using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using TUnit.Assertions.Enums;

namespace Event.Persistence.IntegrationTests.Migrations;

public sealed class MySqlVarcharNarrowingGuardPolicyTests
{
    [Test]
    [Arguments("opaque_sql")]
    [Arguments("create_table")]
    [Arguments("drop_table")]
    [Arguments("rename_table_from")]
    [Arguments("rename_table_to")]
    [Arguments("alter_table")]
    [Arguments("add_column")]
    [Arguments("drop_column")]
    [Arguments("rename_column_from")]
    [Arguments("rename_column_to")]
    [Arguments("insert_data")]
    [Arguments("update_data")]
    [Arguments("second_alter")]
    public async Task SourceChangingBatchCannotProduceExecutableCommands(string dependency)
    {
        await using ExploreDbContext context = CreateContext(PrimaryDatabaseProvider.MySql);
        AlterColumnOperation narrowing = Narrowing("source_table", "value", 2048, 1000);
        MigrationOperation changingSource = dependency switch
        {
            "opaque_sql" => new SqlOperation { Sql = "SELECT 1;" },
            "create_table" => new CreateTableOperation { Name = "source_table" },
            "drop_table" => new DropTableOperation { Name = "source_table" },
            "rename_table_from" => new RenameTableOperation { Name = "source_table", NewName = "other_table" },
            "rename_table_to" => new RenameTableOperation { Name = "other_table", NewName = "source_table" },
            "alter_table" => new AlterTableOperation { Name = "source_table" },
            "add_column" => new AddColumnOperation { Table = "source_table", Name = "value", ClrType = typeof(string) },
            "drop_column" => new DropColumnOperation { Table = "source_table", Name = "value" },
            "rename_column_from" => new RenameColumnOperation { Table = "source_table", Name = "value", NewName = "other_value" },
            "rename_column_to" => new RenameColumnOperation { Table = "source_table", Name = "other_value", NewName = "value" },
            "insert_data" => new InsertDataOperation
            {
                Table = "source_table", Columns = ["value"], Values = new object[,] { { "oversized after preflight" } }
            },
            "update_data" => new UpdateDataOperation
            {
                Table = "source_table", KeyColumns = ["id"], KeyValues = new object[,] { { 1 } },
                Columns = ["value"], Values = new object[,] { { "oversized after preflight" } }
            },
            "second_alter" => Narrowing("source_table", "value", 1000, 500),
            _ => throw new ArgumentOutOfRangeException(nameof(dependency))
        };

        await AssertRejectedAsync(context, [changingSource, narrowing],
            "varchar_narrowing_preflight_dependency_not_supported");
        await AssertRejectedAsync(context, [narrowing, changingSource],
            "varchar_narrowing_preflight_dependency_not_supported");
    }

    [Test]
    [Arguments("both_missing")]
    [Arguments("new_missing")]
    [Arguments("old_missing")]
    [Arguments("both_inconsistent")]
    [Arguments("new_inconsistent")]
    [Arguments("old_inconsistent")]
    [Arguments("column_type_new_missing")]
    [Arguments("column_type_old_missing")]
    [Arguments("column_type_both_missing")]
    public async Task StoreTypeNarrowingRequiresConsistentLengthMetadata(string metadata)
    {
        await using ExploreDbContext context = CreateContext(PrimaryDatabaseProvider.MySql);
        AlterColumnOperation narrowing = Narrowing("source_table", "value", 2048, 1000);
        switch (metadata)
        {
            case "both_missing":
                narrowing.MaxLength = null;
                narrowing.OldColumn.MaxLength = null;
                break;
            case "new_missing":
                narrowing.MaxLength = null;
                break;
            case "old_missing":
                narrowing.OldColumn.MaxLength = null;
                break;
            case "both_inconsistent":
                narrowing.MaxLength = 1500;
                narrowing.OldColumn.MaxLength = 1500;
                break;
            case "new_inconsistent":
                narrowing.MaxLength = 2048;
                break;
            case "old_inconsistent":
                narrowing.OldColumn.MaxLength = 1000;
                break;
            case "column_type_new_missing":
                narrowing.ColumnType = null;
                break;
            case "column_type_old_missing":
                narrowing.OldColumn.ColumnType = null;
                break;
            case "column_type_both_missing":
                narrowing.ColumnType = null;
                narrowing.OldColumn.ColumnType = null;
                break;
        }

        await AssertRejectedAsync(context, [narrowing], "varchar_narrowing_metadata_not_supported");
    }

    [Test]
    [Arguments("charset")]
    [Arguments("collation")]
    [Arguments("unicode")]
    [Arguments("fixed_length")]
    [Arguments("nullable")]
    [Arguments("computed")]
    [Arguments("stored")]
    [Arguments("row_version")]
    public async Task LengthGuardDoesNotClaimToValidateAdditionalConversions(string conversion)
    {
        await using ExploreDbContext context = CreateContext(PrimaryDatabaseProvider.MySql);
        AlterColumnOperation narrowing = Narrowing("source_table", "value", 2048, 1000);
        switch (conversion)
        {
            case "charset":
                narrowing.OldColumn["MySql:CharSet"] = "utf8mb4";
                narrowing["MySql:CharSet"] = "ascii";
                break;
            case "collation":
                narrowing.OldColumn.Collation = "utf8mb4_bin";
                narrowing.Collation = "utf8mb4_general_ci";
                break;
            case "unicode":
                narrowing.OldColumn.IsUnicode = true;
                narrowing.IsUnicode = false;
                break;
            case "fixed_length":
                narrowing.OldColumn.IsFixedLength = false;
                narrowing.IsFixedLength = true;
                break;
            case "nullable":
                narrowing.IsNullable = false;
                break;
            case "computed":
                narrowing.ComputedColumnSql = "CONCAT('recomputed', 'value')";
                break;
            case "stored":
                narrowing.OldColumn.IsStored = false;
                narrowing.IsStored = true;
                break;
            case "row_version":
                narrowing.IsRowVersion = true;
                break;
        }

        await AssertRejectedAsync(context, [narrowing], "varchar_narrowing_conversion_not_supported");
    }

    [Test]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    public async Task PreflightIsStableQuotedAndPrecedesUnchangedOriginalOrder(
        PrimaryDatabaseProvider provider)
    {
        await using ExploreDbContext context = CreateContext(provider);
        AlterColumnOperation first = Narrowing("first`table", "first`value", 1500, 700);
        AlterColumnOperation second = Narrowing("second`table", "second`value", 1700, 900);
        first["Test:RetainedAnnotation"] = "first";
        second["Test:RetainedAnnotation"] = "second";
        var create = new CreateTableOperation { Name = "unrelated_start" };
        create.Columns.Add(new AddColumnOperation
        {
            Table = create.Name, Name = "id", ClrType = typeof(int), ColumnType = "int"
        });
        var drop = new DropTableOperation { Name = "unrelated_middle" };
        MigrationOperation[] operations = [create, first, drop, second];
        string[][] originalAnnotations = operations.Select(Annotations).ToArray();
        IMigrationsSqlGenerator generator = context.GetService<IMigrationsSqlGenerator>();

        foreach (MigrationsSqlGenerationOptions mode in Modes)
        {
            IReadOnlyList<MigrationCommand> commands = generator.Generate(operations, context.Model, mode);
            string script = Join(commands);
            await Assert.That(script).IsEqualTo(Join(generator.Generate(operations, context.Model, mode)));
            string[] guards = Regex.Matches(script, @"ck_varchar_narrowing_[0-9A-F]{32}",
                    RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
                .Select(match => match.Value).ToArray();
            await Assert.That(guards.Length).IsEqualTo(4);
            await Assert.That(guards.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(2);
            int firstCheck = script.IndexOf("CHAR_LENGTH(`first``value`) <= 700", StringComparison.Ordinal);
            int secondCheck = script.IndexOf("CHAR_LENGTH(`second``value`) <= 900", StringComparison.Ordinal);
            int originalCreate = script.IndexOf("CREATE TABLE `unrelated_start`", StringComparison.Ordinal);
            int originalFirstAlter = script.IndexOf("varchar(700)", StringComparison.Ordinal);
            int originalDrop = script.IndexOf("DROP TABLE `unrelated_middle`", StringComparison.Ordinal);
            int originalSecondAlter = script.IndexOf("varchar(900)", StringComparison.Ordinal);
            await Assert.That(firstCheck).IsGreaterThanOrEqualTo(0);
            await Assert.That(secondCheck).IsGreaterThan(firstCheck);
            await Assert.That(originalCreate).IsGreaterThan(secondCheck);
            await Assert.That(originalFirstAlter).IsGreaterThan(originalCreate);
            await Assert.That(originalDrop).IsGreaterThan(originalFirstAlter);
            await Assert.That(originalSecondAlter).IsGreaterThan(originalDrop);
            await Assert.That(script).Contains("ALTER TABLE `first``table`");
            await Assert.That(script).Contains("ALTER TABLE `second``table`");

            foreach (MigrationCommand check in commands.Where(command =>
                command.CommandText.Contains("CHAR_LENGTH(", StringComparison.Ordinal)))
            {
                if (provider == PrimaryDatabaseProvider.MariaDb)
                    await Assert.That(check.CommandText)
                        .StartsWith("SET SESSION check_constraint_checks = ON;");
                else
                    await Assert.That(check.CommandText).DoesNotContain("SET SESSION");
            }
        }

        await Assert.That(operations.Length).IsEqualTo(4);
        await Assert.That(ReferenceEquals(operations[0], create)).IsTrue();
        await Assert.That(ReferenceEquals(operations[1], first)).IsTrue();
        await Assert.That(ReferenceEquals(operations[2], drop)).IsTrue();
        await Assert.That(ReferenceEquals(operations[3], second)).IsTrue();
        for (int index = 0; index < operations.Length; index++)
            await Assert.That(Annotations(operations[index]))
                .IsEquivalentTo(originalAnnotations[index], CollectionOrdering.Matching);
        await Assert.That(first.Table).IsEqualTo("first`table");
        await Assert.That(first.Name).IsEqualTo("first`value");
        await Assert.That(first.MaxLength).IsEqualTo(700);
        await Assert.That(first.ColumnType).IsEqualTo("varchar(700)");
        await Assert.That(first.OldColumn.MaxLength).IsEqualTo(1500);
        await Assert.That(first.OldColumn.ColumnType).IsEqualTo("varchar(1500)");
        await Assert.That(second.MaxLength).IsEqualTo(900);
        await Assert.That(second.OldColumn.MaxLength).IsEqualTo(1700);
    }

    private static readonly MigrationsSqlGenerationOptions[] Modes =
        [MigrationsSqlGenerationOptions.Default, MigrationsSqlGenerationOptions.Script];

    private static async Task AssertRejectedAsync(ExploreDbContext context,
        IReadOnlyList<MigrationOperation> operations, string expectedReason)
    {
        foreach (MigrationsSqlGenerationOptions mode in Modes)
        {
            InvalidOperationException? rejection = null;
            try
            {
                context.GetService<IMigrationsSqlGenerator>().Generate(operations, context.Model, mode);
            }
            catch (InvalidOperationException exception)
            {
                rejection = exception;
            }
            await Assert.That(rejection).IsNotNull();
            await Assert.That(rejection!.Message).IsEqualTo(expectedReason);
        }
    }

    private static string[] Annotations(MigrationOperation operation) => operation.GetAnnotations()
        .Select(annotation => annotation.Name + "=" + annotation.Value)
        .Order(StringComparer.Ordinal).ToArray();

    private static string Join(IEnumerable<MigrationCommand> commands) =>
        string.Join(Environment.NewLine, commands.Select(command => command.CommandText));

    private static AlterColumnOperation Narrowing(string table, string column, int oldLength, int newLength) => new()
    {
        Table = table, Name = column, ClrType = typeof(string), ColumnType = $"varchar({newLength})",
        MaxLength = newLength, IsNullable = true,
        OldColumn = new AddColumnOperation
        {
            ClrType = typeof(string), ColumnType = $"varchar({oldLength})", MaxLength = oldLength, IsNullable = true
        }
    };

    private static ExploreDbContext CreateContext(PrimaryDatabaseProvider provider)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Migrator,
            Provider = provider,
            Host = "database.example.test",
            Database = "varchar_guard_model_only",
            Username = "model_" + Guid.CreateVersion7().ToString("N"),
            Password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)),
            TlsMode = PrimaryDatabaseTlsMode.Required,
            ServerFlavor = provider == PrimaryDatabaseProvider.MariaDb
                ? PrimaryDatabaseServerFlavor.MariaDb : PrimaryDatabaseServerFlavor.MySql,
            ServerVersion = provider == PrimaryDatabaseProvider.MariaDb ? new Version(11, 4) : new Version(8, 4)
        });
        return new ExploreDbContext(options.Options);
    }
}
