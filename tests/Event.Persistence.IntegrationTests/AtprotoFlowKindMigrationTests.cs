using Event.Persistence.IntegrationTests.Database;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Schema;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests;

public sealed class AtprotoFlowKindMigrationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DowngradeAcceptsOnlyRepresentableFlowKindsWithoutPartialSchemaChanges(bool unscoped)
    {
        await using var context = CreateContext();
        var assembly = context.GetService<IMigrationsAssembly>();
        var local = assembly.Migrations.Single(entry => entry.Key.EndsWith("_AddVisitorEnrollment", StringComparison.Ordinal));
        var previous = context.GetService<IModelRuntimeInitializer>().Initialize(
            assembly.CreateMigration(local.Value, context.Database.ProviderName!).TargetModel, designTime: true);
        var current = context.GetService<IDesignTimeModel>().Model;
        var differ = context.GetService<IMigrationsModelDiffer>();
        var currentTable = differ.GetDifferences(null, current.GetRelationalModel())
            .OfType<CreateTableOperation>().Single(operation => operation.Name.EndsWith("atproto_transient_records", StringComparison.Ordinal));
        var generator = context.GetService<IMigrationsSqlGenerator>();
        await context.Database.OpenConnectionAsync();
        foreach (var command in generator.Generate([currentTable], current))
            await context.Database.ExecuteSqlRawAsync(command.CommandText);
        string table = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            current.FindEntityType(typeof(AtprotoTransientRecord))!.GetTableName()!);
        Guid id = Guid.CreateVersion7();
        await using (var insert = context.Database.GetDbConnection().CreateCommand())
        {
            insert.CommandText = $"INSERT INTO {table} (id,purpose,token_digest,tenant_id,flow_kind,protected_payload,expires_at_unix_milliseconds)"
                + " VALUES ($id,1,$digest,$tenant,$kind,$payload,$expires)";
            Add(insert, "$id", id);
            Add(insert, "$digest", new string('a', 64));
            Add(insert, "$tenant", unscoped ? DBNull.Value : Guid.CreateVersion7());
            Add(insert, "$kind", unscoped ? 2 : 1);
            Add(insert, "$payload", "protected-existing-record");
            Add(insert, "$expires", 1_900_000_000_000L);
            await insert.ExecuteNonQueryAsync();
        }
        string before = await ReadSchemaAsync(context);
        var commands = generator.Generate(
            differ.GetDifferences(current.GetRelationalModel(), previous.GetRelationalModel()), previous);
        if (unscoped)
        {
            await Assert.ThrowsAsync<SqliteException>(async () =>
            {
                foreach (var command in commands)
                    await context.Database.ExecuteSqlRawAsync(command.CommandText);
            });
            await Assert.That(await ReadSchemaAsync(context)).IsEqualTo(before);
            await using var retained = context.Database.GetDbConnection().CreateCommand();
            retained.CommandText = $"SELECT flow_kind FROM {table} WHERE id=$id AND tenant_id IS NULL";
            Add(retained, "$id", id);
            await Assert.That(Convert.ToInt64(await retained.ExecuteScalarAsync())).IsEqualTo(2L);
        }
        else
        {
            foreach (var command in commands)
                await context.Database.ExecuteSqlRawAsync(command.CommandText);
            await using var retained = context.Database.GetDbConnection().CreateCommand();
            retained.CommandText = $"SELECT protected_payload FROM {table} WHERE id=$id";
            Add(retained, "$id", id);
            await Assert.That(await retained.ExecuteScalarAsync()).IsEqualTo("protected-existing-record");
        }
    }

    [Test]
    public async Task UpgradePreservesTargetedOAuthAndHandoffWithoutConvertingHealthIntoAuthentication()
    {
        await using var context = CreateContext();
        var assembly = context.GetService<IMigrationsAssembly>();
        var local = assembly.Migrations.Single(entry => entry.Key.EndsWith("_AddVisitorEnrollment", StringComparison.Ordinal));
        var previous = context.GetService<IModelRuntimeInitializer>().Initialize(
            assembly.CreateMigration(local.Value, context.Database.ProviderName!).TargetModel, designTime: true);
        var current = context.GetService<IDesignTimeModel>().Model;
        var differ = context.GetService<IMigrationsModelDiffer>();
        var legacyTable = differ.GetDifferences(null, previous.GetRelationalModel())
            .OfType<CreateTableOperation>().Single(operation => operation.Name.EndsWith("atproto_transient_records", StringComparison.Ordinal));
        var generator = context.GetService<IMigrationsSqlGenerator>();
        await context.Database.OpenConnectionAsync();
        foreach (var command in generator.Generate([legacyTable], previous))
            await context.Database.ExecuteSqlRawAsync(command.CommandText);

        var sql = context.GetService<ISqlGenerationHelper>();
        string table = sql.DelimitIdentifier(current.FindEntityType(typeof(AtprotoTransientRecord))!.GetTableName()!);
        Guid tenant = Guid.CreateVersion7();
        for (int purpose = 1; purpose <= 3; purpose++)
        {
            await using var insert = context.Database.GetDbConnection().CreateCommand();
            insert.CommandText = $"INSERT INTO {table} (id,purpose,token_digest,tenant_id,protected_payload,expires_at_unix_milliseconds)"
                + " VALUES ($id,$purpose,$digest,$tenant,$payload,$expires)";
            Add(insert, "$id", Guid.CreateVersion7());
            Add(insert, "$purpose", purpose);
            Add(insert, "$digest", new string((char)('a' + purpose - 1), 64));
            Add(insert, "$tenant", purpose == 3 ? DBNull.Value : tenant);
            Add(insert, "$payload", "protected-existing-record");
            Add(insert, "$expires", 1_900_000_000_000L);
            await insert.ExecuteNonQueryAsync();
        }

        var upgrade = differ.GetDifferences(previous.GetRelationalModel(), current.GetRelationalModel());
        foreach (var command in generator.Generate(upgrade, current))
            await context.Database.ExecuteSqlRawAsync(command.CommandText);

        await using var read = context.Database.GetDbConnection().CreateCommand();
        read.CommandText = $"SELECT purpose,flow_kind,tenant_id,protected_payload FROM {table} ORDER BY purpose";
        await using var rows = await read.ExecuteReaderAsync();
        for (int purpose = 1; purpose <= 3; purpose++)
        {
            await Assert.That(await rows.ReadAsync()).IsTrue();
            await Assert.That(rows.GetInt32(0)).IsEqualTo(purpose);
            await Assert.That(rows.GetString(3)).IsEqualTo("protected-existing-record");
            if (purpose == 3)
            {
                await Assert.That(rows.IsDBNull(1)).IsTrue();
                await Assert.That(rows.IsDBNull(2)).IsTrue();
            }
            else
            {
                await Assert.That(rows.GetInt32(1)).IsEqualTo(1);
                await Assert.That(Guid.Parse(rows.GetString(2))).IsEqualTo(tenant);
            }
        }
        await Assert.That(await rows.ReadAsync()).IsFalse();
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

    private static void Add(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static async Task<string> ReadSchemaAsync(ExploreDbContext context)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT group_concat(type||':'||name||':'||coalesce(sql,''),'|')
            FROM (SELECT type,name,sql FROM sqlite_master ORDER BY type,name)
            """;
        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The original database schema is absent."));
    }
}
