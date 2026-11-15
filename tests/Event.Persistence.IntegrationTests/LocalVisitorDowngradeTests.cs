using Explore.Persistence;
using Explore.Persistence.Schema;
using Event.Persistence.IntegrationTests.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests;

public sealed class LocalVisitorDowngradeTests
{
    [Test]
    [Arguments("receipt")]
    [Arguments("visitor")]
    [Arguments("unverified-administrator")]
    public async Task IncompatibleDataRefusesDowngradeBeforeAnyTableOrForeignKeyChange(string evidence)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await ExecuteAsync(connection, """
            CREATE TABLE ie_visitor_enrollment_operations (id TEXT NOT NULL PRIMARY KEY);
            CREATE TABLE ie_local_identity_credential_operation (
                id TEXT NOT NULL PRIMARY KEY, kind INTEGER NOT NULL,
                initiating_application_user_id TEXT, verified_by_application_user_id TEXT,
                verified_at TEXT, visitor_enrollment_operation_id TEXT);
            """);
        string id = Guid.CreateVersion7().ToString();
        if (evidence == "receipt")
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO ie_visitor_enrollment_operations (id) VALUES ($id)";
            insert.Parameters.AddWithValue("$id", id);
            await insert.ExecuteNonQueryAsync();
        }
        else
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO ie_local_identity_credential_operation (id,kind,visitor_enrollment_operation_id)
                VALUES ($id,$kind,$origin)
                """;
            insert.Parameters.AddWithValue("$id", id);
            insert.Parameters.AddWithValue("$kind", evidence == "visitor" ? 3 : 1);
            insert.Parameters.AddWithValue("$origin", evidence == "visitor" ? id : DBNull.Value);
            await insert.ExecuteNonQueryAsync();
        }
        await using var context = CreateContext();
        var (migration, previous) = ReadMigration(context);
        var commands = context.GetService<IMigrationsSqlGenerator>()
            .Generate(migration.DownOperations, previous);
        await Assert.ThrowsAsync<SqliteException>(async () =>
        {
            foreach (var command in commands)
                await ExecuteAsync(connection, command.CommandText);
        });
        await using var state = connection.CreateCommand();
        state.CommandText = """
            SELECT (SELECT count(*) FROM sqlite_master
                    WHERE type='table' AND name='ie_visitor_enrollment_operations')=1
                AND (SELECT count(*) FROM sqlite_master
                    WHERE type='table' AND name='ie_local_identity_credential_operation')=1
                AND (SELECT foreign_keys FROM pragma_foreign_keys)=1
            """;
        await Assert.That(Convert.ToInt64(await state.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture)).IsEqualTo(1L);
        await using var retained = connection.CreateCommand();
        retained.CommandText = evidence == "receipt"
            ? "SELECT id FROM ie_visitor_enrollment_operations"
            : "SELECT id FROM ie_local_identity_credential_operation WHERE initiating_application_user_id IS NULL AND verified_by_application_user_id IS NULL AND verified_at IS NULL";
        await Assert.That(await retained.ExecuteScalarAsync()).IsEqualTo(id);
    }

    [Test]
    public async Task EmptyVisitorStateCanDowngradeWithoutAdministratorDefaults()
    {
        await using var context = CreateContext();
        await context.Database.OpenConnectionAsync();
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        await ExecuteAsync(connection, context.Database.GenerateCreateScript());
        var (migration, previous) = ReadMigration(context);
        foreach (var command in context.GetService<IMigrationsSqlGenerator>()
                     .Generate(migration.DownOperations, previous))
            await ExecuteAsync(connection, command.CommandText);
        await using var state = connection.CreateCommand();
        state.CommandText = """
            SELECT (SELECT count(*) FROM sqlite_master
                    WHERE type='table' AND name='ie_visitor_enrollment_operations')=0
                AND (SELECT count(*) FROM pragma_table_info('ie_local_identity_credential_operation')
                    WHERE name='visitor_enrollment_operation_id')=0
                AND (SELECT count(*) FROM pragma_table_info('ie_local_identity_credential_operation')
                    WHERE name IN ('initiating_application_user_id','verified_by_application_user_id','verified_at')
                        AND "notnull"=1 AND dflt_value IS NULL)=3
                AND (SELECT foreign_keys FROM pragma_foreign_keys)=1
            """;
        await Assert.That(Convert.ToInt64(await state.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture)).IsEqualTo(1L);
    }

    [Test]
    public async Task SqlGenerationRestoresItsOriginalOperationsAfterRemovingUnsafeDefaults()
    {
        await using var context = CreateContext();
        var (migration, previous) = ReadMigration(context);
        var operations = migration.DownOperations;
        var original = operations.OfType<AlterColumnOperation>()
            .Select(operation => (operation, operation.DefaultValue, operation.DefaultValueSql)).ToArray();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var first = generator.Generate(operations, previous).Select(command => command.CommandText).ToArray();
        var second = generator.Generate(operations, previous).Select(command => command.CommandText).ToArray();
        await Assert.That(second).IsEquivalentTo(first);
        foreach (var (operation, value, sql) in original)
        {
            await Assert.That(operation.DefaultValue).IsEqualTo(value);
            await Assert.That(operation.DefaultValueSql).IsEqualTo(sql);
        }
    }

    private static ExploreDbContext CreateContext()
    {
        using var provider = ExploreDbContextModelProviderTests.CreateContext("Sqlite");
        var options = TestDbContextOptions.Create(
            (DbContextOptions<ExploreDbContext>)provider.GetService<IDbContextOptions>());
        options.UseSqlite("Data Source=:memory:", database =>
            database.MigrationsAssembly("Explore.Persistence.Migrations.Sqlite"));
        options.UseSnakeCaseNamingConvention();
        options.ReplaceService<IMigrationsSqlGenerator, ConfigurableSqliteMigrationsSqlGenerator>();
        return new(options.Options);
    }

    private static (Migration Migration, Microsoft.EntityFrameworkCore.Metadata.IModel Previous) ReadMigration(
        ExploreDbContext context)
    {
        var assembly = context.GetService<IMigrationsAssembly>();
        var entries = assembly.Migrations.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray();
        int index = Array.FindIndex(entries, entry => entry.Key.EndsWith("_AddVisitorEnrollment", StringComparison.Ordinal));
        if (index <= 0) throw new InvalidOperationException("The generated visitor migration and predecessor are required.");
        var migration = assembly.CreateMigration(entries[index].Value, context.Database.ProviderName!);
        var previous = assembly.CreateMigration(entries[index - 1].Value, context.Database.ProviderName!);
        return (migration, context.GetService<IModelRuntimeInitializer>().Initialize(previous.TargetModel, designTime: true));
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
