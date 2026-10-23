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
[NotInParallel("UnsupportedVarcharNarrowingMetadataDb")]
public sealed class UnsupportedVarcharNarrowingMetadataTests(MySqlMigrationGuardFixture fixture)
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public Task MySql_MetadataCannotBypassNarrowingSafety(bool exportedScript, bool inconsistent) =>
        AssertRejectedBeforeDdlAsync(PrimaryDatabaseProvider.MySql, exportedScript, inconsistent);

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public Task MariaDb_MetadataCannotBypassNarrowingSafety(bool exportedScript, bool inconsistent) =>
        AssertRejectedBeforeDdlAsync(PrimaryDatabaseProvider.MariaDb, exportedScript, inconsistent);

    private async Task AssertRejectedBeforeDdlAsync(
        PrimaryDatabaseProvider provider, bool exportedScript, bool inconsistent)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options,
            await fixture.CreateOptionsAsync(provider));
        await using var context = new ExploreDbContext(options.Options);
        await context.Database.OpenConnectionAsync();
        DbConnection connection = context.Database.GetDbConnection();
        string tableName = "metadata_narrowing_" + Guid.NewGuid().ToString("N");
        ISqlGenerationHelper sql = context.GetService<ISqlGenerationHelper>();
        string table = sql.DelimitIdentifier(tableName);
        string column = sql.DelimitIdentifier("value");
        string originalMode = Convert.ToString(await ScalarAsync(connection,
            "SELECT @@SESSION.sql_mode"), CultureInfo.InvariantCulture)!;
        string original = new('x', 1001);
        try
        {
            await ExecuteAsync(connection, "SET SESSION sql_mode = ''");
            await Assert.That(await ScalarAsync(connection, "SELECT @@SESSION.sql_mode"))
                .IsEqualTo(string.Empty);
            await ExecuteAsync(connection, $"CREATE TABLE {table} ({column} varchar(2048) NULL) "
                + "ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
            await ExecuteAsync(connection, $"INSERT INTO {table} ({column}) VALUES (@value)",
                ("@value", original));
            var narrowing = new AlterColumnOperation
            {
                Table = tableName,
                Name = "value",
                ClrType = typeof(string),
                ColumnType = "varchar(1000)",
                MaxLength = inconsistent ? 1500 : null,
                IsNullable = true,
                OldColumn = new AddColumnOperation
                {
                    ClrType = typeof(string),
                    ColumnType = "varchar(2048)",
                    MaxLength = inconsistent ? 1500 : null,
                    IsNullable = true
                }
            };
            InvalidOperationException? rejection = null;
            bool executableCommandsProduced = false;
            try
            {
                IReadOnlyList<MigrationCommand> commands = context.GetService<IMigrationsSqlGenerator>()
                    .Generate([narrowing], context.Model, exportedScript
                        ? MigrationsSqlGenerationOptions.Script : MigrationsSqlGenerationOptions.Default);
                executableCommandsProduced = true;
                if (exportedScript)
                    await ExecuteAsync(connection, string.Join(Environment.NewLine,
                        commands.Select(command => command.CommandText)));
                else
                    foreach (MigrationCommand command in commands)
                        await command.ExecuteNonQueryAsync(context.GetService<IRelationalConnection>());
            }
            catch (InvalidOperationException exception)
            {
                rejection = exception;
            }

            await Assert.That(Convert.ToInt64(await ScalarAsync(connection,
                "SELECT character_maximum_length FROM information_schema.columns "
                + "WHERE table_schema = DATABASE() AND table_name = @table AND column_name = 'value'",
                ("@table", tableName)), CultureInfo.InvariantCulture)).IsEqualTo(2048L);
            await Assert.That(await ScalarAsync(connection, $"SELECT HEX({column}) FROM {table}"))
                .IsEqualTo(Convert.ToHexString(Encoding.UTF8.GetBytes(original)));
            await Assert.That(executableCommandsProduced).IsFalse();
            await Assert.That(rejection).IsNotNull();
            await Assert.That(rejection!.Message).IsEqualTo("varchar_narrowing_metadata_not_supported");
        }
        finally
        {
            await ExecuteAsync(connection, $"DROP TABLE IF EXISTS {table}");
            await ExecuteAsync(connection, "SET SESSION sql_mode = @mode", ("@mode", originalMode));
        }
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql,
        params (string Name, object Value)[] parameters)
    {
        await using DbCommand command = Command(connection, sql, parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(DbConnection connection, string sql,
        params (string Name, object Value)[] parameters)
    {
        await using DbCommand command = Command(connection, sql, parameters);
        return await command.ExecuteScalarAsync();
    }

    private static DbCommand Command(DbConnection connection, string sql,
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
