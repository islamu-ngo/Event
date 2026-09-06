// ABOUTME: Proves generated initials install semantic constraints on non-transactional DDL providers.
// ABOUTME: Exercises fresh application and idempotent reapplication on MariaDB and MySQL.

#nullable enable

using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Schema;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;

namespace Event.Persistence.IntegrationTests.Migrations;

[ClassDataSource<SemanticValueNonTransactionalProviderFixture>(
    Shared = SharedType.PerClass)]
[NotInParallel("SemanticValueNonTransactionalProviderDb")]
public sealed class SemanticValueNonTransactionalMigrationTests(
    SemanticValueNonTransactionalProviderFixture fixture)
{
    private static readonly string[] ConstraintNames =
    [
        "ck_event_agenda_item_local_date_range",
        "ck_event_session_local_date_range",
        "ck_event_ticket_type_money_nonnegative",
        "ck_location_pii_coordinate_shape"
    ];

    [Test]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    public async Task InitialMigration_AppliesSemanticConstraintsExactlyOnce(
        PrimaryDatabaseProvider provider)
    {
        await using ExploreDbContext context = CreateContext(
            fixture.CreateOptions(provider));

        await ExploreDatabaseMigrator.MigrateAsync(
            context,
            new ConfigurationManager());

        await Assert.That(await CountSemanticConstraintsAsync(context))
            .IsEqualTo(ConstraintNames.Length);
        string[] applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        string[] available = context.Database.GetMigrations().ToArray();
        await Assert.That(applied).IsEquivalentTo(available);
        await Assert.That(applied.Count(migration => migration.EndsWith("_Init", StringComparison.Ordinal))).IsEqualTo(1);

        await ExploreDatabaseMigrator.MigrateAsync(
            context,
            new ConfigurationManager());

        await Assert.That(await CountSemanticConstraintsAsync(context))
            .IsEqualTo(ConstraintNames.Length);
        await Assert.That(await context.Database.GetAppliedMigrationsAsync())
            .IsEquivalentTo(applied);

        await context.GetService<IMigrator>().MigrateAsync(Migration.InitialDatabase);
        await Assert.That(await CountSemanticConstraintsAsync(context)).IsEqualTo(0);

        await ExploreDatabaseMigrator.MigrateAsync(
            context,
            new ConfigurationManager());
        await Assert.That(await CountSemanticConstraintsAsync(context))
            .IsEqualTo(ConstraintNames.Length);
        await Assert.That(await context.Database.GetAppliedMigrationsAsync())
            .IsEquivalentTo(applied);

        await SqliteApplicationInitialLifecycleTests.AssertDataProtectionLifecycleAsync(
            fixture.CreateOptions(provider));
    }

    private static ExploreDbContext CreateContext(
        PrimaryDatabaseConnectionOptions options)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, options);
        return new ExploreDbContext(builder.Options);
    }

    private static async Task<int> CountSemanticConstraintsAsync(
        ExploreDbContext context)
    {
        object? result = await ExecuteScalarAsync(
            context,
            """
            SELECT COUNT(*)
            FROM information_schema.table_constraints
            WHERE constraint_schema = DATABASE()
              AND constraint_name IN (
                  'ck_event_agenda_item_local_date_range',
                  'ck_event_session_local_date_range',
                  'ck_event_ticket_type_money_nonnegative',
                  'ck_location_pii_coordinate_shape')
            """);
        return Convert.ToInt32(
            result,
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(
        ExploreDbContext context,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using DbCommand command = CreateCommand(context, sql, parameters);
        await EnsureOpenAsync(command.Connection!);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ExecuteScalarAsync(
        ExploreDbContext context,
        string sql)
    {
        await using DbCommand command = CreateCommand(context, sql, []);
        await EnsureOpenAsync(command.Connection!);
        return await command.ExecuteScalarAsync();
    }

    private static DbCommand CreateCommand(
        ExploreDbContext context,
        string sql,
        (string Name, object Value)[] parameters)
    {
        DbCommand command =
            context.Database.GetDbConnection().CreateCommand();
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

    private static async Task EnsureOpenAsync(DbConnection connection)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
    }
}
