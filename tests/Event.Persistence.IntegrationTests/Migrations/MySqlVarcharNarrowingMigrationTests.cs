#nullable enable

using System.Data.Common;
using System.Globalization;
using System.Text;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Migrations;

[ClassDataSource<MySqlMigrationGuardFixture>(Shared = SharedType.PerClass)]
[NotInParallel("MySqlVarcharNarrowingMigrationDb")]
public sealed class MySqlVarcharNarrowingMigrationTests(MySqlMigrationGuardFixture fixture)
{
    [Test]
    [Arguments(false, "nonspace")]
    [Arguments(true, "nonspace")]
    [Arguments(false, "trailing")]
    [Arguments(true, "trailing")]
    [Arguments(false, "multibyte")]
    [Arguments(true, "multibyte")]
    public Task MySql_OverflowInLastTableRejectsBeforeAnyColumnNarrows(
        bool exportedScript, string overflowKind) =>
        AssertNarrowingAsync(PrimaryDatabaseProvider.MySql, exportedScript, overflowKind);

    [Test]
    [Arguments(false, "nonspace")]
    [Arguments(true, "nonspace")]
    [Arguments(false, "trailing")]
    [Arguments(true, "trailing")]
    [Arguments(false, "multibyte")]
    [Arguments(true, "multibyte")]
    public Task MariaDb_DisabledChecksAndLastTableOverflowRejectBeforeAnyColumnNarrows(
        bool exportedScript, string overflowKind) =>
        AssertNarrowingAsync(PrimaryDatabaseProvider.MariaDb, exportedScript, overflowKind);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task MySql_FittingValuesNarrowWithoutChangingStoredBytes(bool exportedScript) =>
        AssertNarrowingAsync(PrimaryDatabaseProvider.MySql, exportedScript, null);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task MariaDb_FittingValuesNarrowWithoutChangingStoredBytes(bool exportedScript) =>
        AssertNarrowingAsync(PrimaryDatabaseProvider.MariaDb, exportedScript, null);

    private async Task AssertNarrowingAsync(
        PrimaryDatabaseProvider provider,
        bool exportedScript,
        string? overflowKind)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options,
            await fixture.CreateOptionsAsync(provider));
        await using var context = new ExploreDbContext(options.Options);
        await context.Database.OpenConnectionAsync();
        DbConnection connection = context.Database.GetDbConnection();
        string originalMode = Convert.ToString(await ScalarAsync(connection,
            "SELECT @@SESSION.sql_mode"), CultureInfo.InvariantCulture)!;
        object? originalChecks = provider == PrimaryDatabaseProvider.MariaDb
            ? await ScalarAsync(connection, "SELECT @@SESSION.check_constraint_checks")
            : null;
        object? globalMode = await ScalarAsync(connection, "SELECT @@GLOBAL.sql_mode");
        string prefix = "narrow_" + Guid.NewGuid().ToString("N");
        string[] tables = Enumerable.Range(0, 5).Select(index => prefix + "_" + index).ToArray();
        ISqlGenerationHelper sql = context.GetService<ISqlGenerationHelper>();
        string[] fittingValues = [new string('x', 1000), new string(' ', 1000), RepeatUnicode(1000)];
        string? overflow = overflowKind switch
        {
            null => null,
            "nonspace" => new string('x', 1001),
            "trailing" => new string('x', 1000) + " ",
            "multibyte" => RepeatUnicode(1001),
            _ => throw new ArgumentOutOfRangeException(nameof(overflowKind))
        };

        try
        {
            await ExecuteAsync(connection, "SET SESSION sql_mode = ''");
            await Assert.That(await ScalarAsync(connection, "SELECT @@SESSION.sql_mode"))
                .IsEqualTo(string.Empty);
            if (provider == PrimaryDatabaseProvider.MariaDb)
            {
                await ExecuteAsync(connection, "SET SESSION check_constraint_checks = OFF");
                await Assert.That(Convert.ToInt32(await ScalarAsync(connection,
                    "SELECT @@SESSION.check_constraint_checks"), CultureInfo.InvariantCulture)).IsEqualTo(0);
            }

            foreach (string table in tables)
            {
                await ExecuteAsync(connection, $"CREATE TABLE {sql.DelimitIdentifier(table)} "
                    + "(id int NOT NULL PRIMARY KEY, pattern varchar(2048) NULL) "
                    + "ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
                await ExecuteAsync(connection, $"INSERT INTO {sql.DelimitIdentifier(table)} "
                    + "(id, pattern) VALUES (0, NULL)");
                for (int index = 0; index < fittingValues.Length; index++)
                    await ExecuteAsync(connection, $"INSERT INTO {sql.DelimitIdentifier(table)} "
                        + "(id, pattern) VALUES (@id, @value)",
                        ("@id", index + 1), ("@value", fittingValues[index]));
            }
            if (overflow is not null)
                await ExecuteAsync(connection, $"INSERT INTO {sql.DelimitIdentifier(tables[^1])} "
                    + "(id, pattern) VALUES (4, @value)", ("@value", overflow));

            var operations = tables.Select(table => (MigrationOperation)new AlterColumnOperation
            {
                Table = table,
                Name = "pattern",
                ClrType = typeof(string),
                ColumnType = "varchar(1000)",
                MaxLength = 1000,
                IsNullable = true,
                OldColumn = new AddColumnOperation
                {
                    ClrType = typeof(string),
                    ColumnType = "varchar(2048)",
                    MaxLength = 2048,
                    IsNullable = true
                }
            }).ToArray();
            IReadOnlyList<MigrationCommand> commands = context.GetService<IMigrationsSqlGenerator>()
                .Generate(operations, context.Model, exportedScript
                    ? MigrationsSqlGenerationOptions.Script
                    : MigrationsSqlGenerationOptions.Default);
            DbException? rejection = null;
            try
            {
                if (exportedScript)
                    await ExecuteAsync(connection, string.Join(Environment.NewLine,
                        commands.Select(command => command.CommandText)));
                else
                    foreach (MigrationCommand command in commands)
                        await command.ExecuteNonQueryAsync(context.GetService<IRelationalConnection>());
            }
            catch (DbException exception)
            {
                rejection = exception;
            }

            foreach (string table in tables)
            {
                long width = Convert.ToInt64(await ScalarAsync(connection,
                    "SELECT character_maximum_length FROM information_schema.columns "
                    + "WHERE table_schema = DATABASE() AND table_name = @table AND column_name = 'pattern'",
                    ("@table", table)), CultureInfo.InvariantCulture);
                await Assert.That(width).IsEqualTo(overflow is null ? 1000L : 2048L);
                await Assert.That(await ScalarAsync(connection,
                    $"SELECT pattern FROM {sql.DelimitIdentifier(table)} WHERE id = 0")).IsEqualTo(DBNull.Value);
                for (int index = 0; index < fittingValues.Length; index++)
                    await AssertHexAsync(connection, sql, table, index + 1, fittingValues[index]);
            }
            if (overflow is not null)
            {
                await AssertHexAsync(connection, sql, tables[^1], 4, overflow);
                await Assert.That(rejection).IsNotNull();
                await Assert.That(rejection!.Message).Contains("ck_varchar_narrowing_");
            }
            else
            {
                await Assert.That(rejection).IsNull();
                foreach (string table in tables)
                    await Assert.That(Convert.ToInt32(await ScalarAsync(connection,
                        "SELECT COUNT(*) FROM information_schema.table_constraints "
                        + "WHERE constraint_schema = DATABASE() AND table_name = @table "
                        + "AND constraint_type = 'CHECK'", ("@table", table)),
                        CultureInfo.InvariantCulture)).IsEqualTo(0);
            }
            await Assert.That(await ScalarAsync(connection, "SELECT @@SESSION.sql_mode"))
                .IsEqualTo(string.Empty);
            await Assert.That(await ScalarAsync(connection, "SELECT @@GLOBAL.sql_mode"))
                .IsEqualTo(globalMode);
        }
        finally
        {
            foreach (string table in tables)
                await ExecuteAsync(connection, $"DROP TABLE IF EXISTS {sql.DelimitIdentifier(table)}");
            await ExecuteAsync(connection, "SET SESSION sql_mode = @mode", ("@mode", originalMode));
            if (originalChecks is not null)
                await ExecuteAsync(connection, "SET SESSION check_constraint_checks = @checks",
                    ("@checks", originalChecks));
        }
    }

    private static string RepeatUnicode(int count) =>
        string.Concat(Enumerable.Repeat("\U0001F642", count));

    private static async Task AssertHexAsync(DbConnection connection, ISqlGenerationHelper sql,
        string table, int id, string expected)
    {
        object? actual = await ScalarAsync(connection,
            $"SELECT HEX(pattern) FROM {sql.DelimitIdentifier(table)} WHERE id = @id", ("@id", id));
        await Assert.That(actual).IsEqualTo(Convert.ToHexString(Encoding.UTF8.GetBytes(expected)));
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql,
        params (string Name, object Value)[] parameters)
    {
        await using DbCommand command = CreateCommand(connection, sql, parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(DbConnection connection, string sql,
        params (string Name, object Value)[] parameters)
    {
        await using DbCommand command = CreateCommand(connection, sql, parameters);
        return await command.ExecuteScalarAsync();
    }

    private static DbCommand CreateCommand(DbConnection connection, string sql,
        (string Name, object Value)[] parameters)
    {
        DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
        return command;
    }
}
