// ABOUTME: Verifies the application migration seam and MigrationService topology orchestration.
// ABOUTME: Proves retry safety and one authority migration path without requiring every provider runtime.

#nullable enable

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Configuration;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Schema;
using Explore.Secrets.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Migrations;

[ClassDataSource<RecipientDeliveryMigrationContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("RecipientDeliveryMigrationDb")]
[Property("Category", "ExploreDatabaseMigrator")]
public sealed class ExploreDatabaseMigratorTests(RecipientDeliveryMigrationContainerFixture fixture)
{
    [Test]
    public async Task MigrateAsync_AppliesCurrentMigrationSetWithoutStageConfiguration_AndIsRetrySafe()
    {
        string databaseName = $"ordinary_migrator_{Guid.NewGuid():N}";
        string connectionString = await CreateDatabaseAsync(databaseName);

        try
        {
            await using ExploreDbContext context = CreateContext(connectionString);
            var configuration = new ConfigurationManager();

            await ExploreDatabaseMigrator.MigrateAsync(context, configuration);
            string[] firstHistory = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            bool probeTableExists = await context.Database.SqlQueryRaw<bool>(
                    """SELECT to_regclass('public.task4_migration_probe') IS NOT NULL AS "Value" """)
                .SingleAsync();

            await ExploreDatabaseMigrator.MigrateAsync(context, configuration);
            string[] secondHistory = (await context.Database.GetAppliedMigrationsAsync()).ToArray();

            await Assert.That(firstHistory).IsEquivalentTo([Task4MigrationProbe.MigrationId]);
            await Assert.That(probeTableExists).IsTrue();
            await Assert.That(secondHistory).IsEquivalentTo(firstHistory);
        }
        finally
        {
            await DropDatabaseAsync(databaseName);
        }
    }

    [Test]
    [Arguments(PrivacyErasureAuthorityTopology.CoLocated, "AuthorityCoLocatedPostgreSql")]
    [Arguments(PrivacyErasureAuthorityTopology.ExternalDatabase, "AuthorityExternalDatabasePostgreSql")]
    [TUnit.Core.Executors.TestExecutor<FreshEfProcessExecutor>]
    public async Task MigrateAndSeedAsync_PostgreSqlAppliesExactlyOneAuthorityPath(
        PrivacyErasureAuthorityTopology topology,
        string authorityOperation)
    {
        string primaryDatabaseName = $"migrator_primary_{Guid.NewGuid():N}";
        string primaryConnectionString = await CreateDatabaseAsync(primaryDatabaseName);
        string? authorityDatabaseName = topology == PrivacyErasureAuthorityTopology.ExternalDatabase
            ? $"migrator_authority_{Guid.NewGuid():N}"
            : null;
        string? authorityConnectionString = authorityDatabaseName is null
            ? null
            : await CreateDatabaseAsync(authorityDatabaseName);
        var migrationDatabase = PostgresOptions(primaryConnectionString, PrimaryDatabaseRole.Migrator);
        var runtimeDatabase = PostgresOptions(primaryConnectionString, PrimaryDatabaseRole.Runtime);
        IConfiguration configuration = PostgresTopologyConfiguration(topology, authorityConnectionString);
        var logger = new MigrationOperationLogger();

        try
        {
            await using ExploreDbContext runtime = CreatePostgresApplicationContext(runtimeDatabase);
            await ExploreDatabaseMigrator.MigrateAndSeedAsync(
                runtime,
                ProductionHostEnvironment.Instance,
                configuration,
                migrationDatabase,
                logger);

            await AssertOperationsAsync(logger, authorityOperation);
            await ExploreDatabaseMigratorTopologyTests.AssertMigrationSetCompleteAsync(runtime);
            await Assert.That(await runtime.LocationKinds.AnyAsync()).IsTrue();

            await using DataProtectionKeyContext dataProtection = CreatePostgresDataProtectionContext(migrationDatabase);
            await ExploreDatabaseMigratorTopologyTests.AssertMigrationSetCompleteAsync(dataProtection);

            if (topology == PrivacyErasureAuthorityTopology.CoLocated)
            {
                var options = TestDbContextOptions.Create<CoLocatedPrivacyErasureAuthorityDbContext>();
                PrimaryDatabaseProviderComposition.ConfigureCoLocatedPrivacyErasureAuthority(options, migrationDatabase);
                await using var authority = new CoLocatedPrivacyErasureAuthorityDbContext(options.Options);
                await ExploreDatabaseMigratorTopologyTests.AssertMigrationSetCompleteAsync(authority);
            }
            else
            {
                await using PrivacyErasureAuthorityDbContext authority =
                    new PrivacyErasureAuthorityDbContextFactory().CreateDbContext(configuration);
                await ExploreDatabaseMigratorTopologyTests.AssertMigrationSetCompleteAsync(authority);

                var unselectedOptions = TestDbContextOptions.Create<CoLocatedPrivacyErasureAuthorityDbContext>();
                PrimaryDatabaseProviderComposition.ConfigureCoLocatedPrivacyErasureAuthority(
                    unselectedOptions,
                    migrationDatabase);
                await using var unselectedAuthority =
                    new CoLocatedPrivacyErasureAuthorityDbContext(unselectedOptions.Options);
                await Assert.That(await unselectedAuthority.Database.GetAppliedMigrationsAsync()).IsEmpty();
            }
        }
        finally
        {
            if (authorityDatabaseName is not null)
            {
                await DropDatabaseAsync(authorityDatabaseName);
            }
            await DropDatabaseAsync(primaryDatabaseName);
        }
    }

    [Test]
    [TUnit.Core.Executors.TestExecutor<FreshEfProcessExecutor>]
    public async Task MigrateAndSeedAsync_ExternalAuthoritySameTarget_FailsBeforeMigrationIo()
    {
        string databaseName = $"migrator_same_target_{Guid.NewGuid():N}";
        string connectionString = await CreateDatabaseAsync(databaseName);
        var migrationDatabase = PostgresOptions(connectionString, PrimaryDatabaseRole.Migrator);
        var runtimeDatabase = PostgresOptions(connectionString, PrimaryDatabaseRole.Runtime);
        string migrationHost = migrationDatabase.Host
            ?? throw new InvalidOperationException("The PostgreSQL test fixture must provide a host.");
        string migrationTargetDatabase = migrationDatabase.Database
            ?? throw new InvalidOperationException("The PostgreSQL test fixture must provide a database.");
        string migrationUsername = migrationDatabase.Username
            ?? throw new InvalidOperationException("The PostgreSQL test fixture must provide a username.");
        string migrationPassword = migrationDatabase.Password
            ?? throw new InvalidOperationException("The PostgreSQL test fixture must provide a password.");
        IConfiguration configuration = PostgresTopologyConfiguration(
            PrivacyErasureAuthorityTopology.ExternalDatabase,
            connectionString);
        var logger = new MigrationOperationLogger();

        try
        {
            await using ExploreDbContext runtime = CreatePostgresApplicationContext(runtimeDatabase);

            OptionsValidationException? exception = await Assert.That(async () =>
                    await ExploreDatabaseMigrator.MigrateAndSeedAsync(
                        runtime,
                        ProductionHostEnvironment.Instance,
                        configuration,
                        migrationDatabase,
                        logger))
                .Throws<OptionsValidationException>();

            OptionsValidationException validationException = exception
                ?? throw new InvalidOperationException("The same-target preflight must fail validation.");
            string diagnostic = validationException.Message;
            await Assert.That(diagnostic.Length).IsLessThanOrEqualTo(512);
            await Assert.That(diagnostic)
                .Contains("different physical PostgreSQL database", StringComparison.OrdinalIgnoreCase);
            string[] sensitiveValues =
                [migrationHost, migrationTargetDatabase, migrationUsername, migrationPassword, connectionString];
            foreach (string sensitiveValue in sensitiveValues)
            {
                await Assert.That(diagnostic).DoesNotContain(sensitiveValue);
            }
            await Assert.That(logger.Operations).IsEmpty();
            await Assert.That(await runtime.Database.GetAppliedMigrationsAsync()).IsEmpty();

            await using DataProtectionKeyContext dataProtection =
                CreatePostgresDataProtectionContext(migrationDatabase);
            await Assert.That(await dataProtection.Database.GetAppliedMigrationsAsync()).IsEmpty();

            await using PrivacyErasureAuthorityDbContext authority =
                new PrivacyErasureAuthorityDbContextFactory().CreateDbContext(configuration);
            await Assert.That(await authority.Database.GetAppliedMigrationsAsync()).IsEmpty();
        }
        finally
        {
            await DropDatabaseAsync(databaseName);
        }
    }

    private async Task<string> CreateDatabaseAsync(string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Database = "postgres",
            Pooling = false
        };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
        await command.ExecuteNonQueryAsync();
        builder.Database = databaseName;
        return builder.ConnectionString;
    }

    private async Task DropDatabaseAsync(string databaseName)
    {
        NpgsqlConnection.ClearAllPools();
        var builder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Database = "postgres",
            Pooling = false
        };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var terminate = new NpgsqlCommand(
            "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @database AND pid <> pg_backend_pid()",
            connection);
        terminate.Parameters.AddWithValue("database", databaseName);
        await terminate.ExecuteNonQueryAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\"", connection);
        await drop.ExecuteNonQueryAsync();
    }

    private static ExploreDbContext CreateContext(string connectionString)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(
                connectionString,
                postgres => postgres.MigrationsAssembly(typeof(Task4MigrationProbe).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(warnings =>
            {
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning);
            });
        return new Task4MigrationProbeContext(builder.Options);
    }

    private static PrimaryDatabaseConnectionOptions PostgresOptions(
        string connectionString,
        PrimaryDatabaseRole role)
    {
        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        return new PrimaryDatabaseConnectionOptions
        {
            Role = role,
            Provider = PrimaryDatabaseProvider.PostgreSql,
            Host = connection.Host,
            Port = connection.Port,
            Database = connection.Database,
            Username = connection.Username,
            Password = connection.Password,
            TlsMode = PrimaryDatabaseTlsMode.Disabled,
        };
    }

    private static IConfiguration PostgresTopologyConfiguration(
        PrivacyErasureAuthorityTopology topology,
        string? authorityConnectionString)
    {
        var values = new Dictionary<string, string?>
        {
            ["PrivacyErasure:Authority:Topology"] = topology.ToString(),
        };
        if (authorityConnectionString is not null)
        {
            var authority = new NpgsqlConnectionStringBuilder(authorityConnectionString);
            values["PrivacyErasureAuthorityDatabase:Provider"] = "PostgreSql";
            values["PrivacyErasureAuthorityDatabase:Host"] = authority.Host;
            values["PrivacyErasureAuthorityDatabase:Port"] = authority.Port.ToString();
            values["PrivacyErasureAuthorityDatabase:Database"] = authority.Database;
            values["PrivacyErasureAuthorityDatabase:TlsMode"] = "Disabled";
            values["PrivacyErasureAuthorityDatabase:Migrator:Username"] = authority.Username;
            values["PrivacyErasureAuthorityDatabase:Migrator:Password"] = authority.Password;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static ExploreDbContext CreatePostgresApplicationContext(
        PrimaryDatabaseConnectionOptions database)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, database);
        return new ExploreDbContext(options.Options);
    }

    private static DataProtectionKeyContext CreatePostgresDataProtectionContext(
        PrimaryDatabaseConnectionOptions database)
    {
        var options = TestDbContextOptions.Create<DataProtectionKeyContext>();
        PrimaryDatabaseProviderComposition.ConfigureDataProtection(options, database);
        return new DataProtectionKeyContext(options.Options);
    }

    private static async Task AssertOperationsAsync(
        MigrationOperationLogger logger,
        string authorityOperation)
    {
        string[] expected =
            ["Application", "ProviderAdjustments", "DataProtection", authorityOperation, "Seed"];
        await Assert.That(logger.Operations.Count).IsEqualTo(expected.Length);
        for (int index = 0; index < expected.Length; index++)
        {
            await Assert.That(logger.Operations[index]).IsEqualTo(expected[index]);
        }
    }
}

public sealed class Task4MigrationProbeContext(DbContextOptions<ExploreDbContext> options)
    : ExploreDbContext(options);

[DbContext(typeof(Task4MigrationProbeContext))]
[Migration(MigrationId)]
public sealed class Task4MigrationProbe : Migration
{
    public const string MigrationId = "20260720000000_Task4MigrationProbe";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "task4_migration_probe",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table => table.PrimaryKey("pk_task4_migration_probe", x => x.id));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("task4_migration_probe");
    }
}

[Property("Category", "ExploreDatabaseMigratorTopology")]
[NotInParallel("ExploreDatabaseMigratorSqlite")]
public sealed class ExploreDatabaseMigratorTopologyTests
{
    [Test]
    [Arguments(PrivacyErasureAuthorityTopology.EmbeddedSqlite, "AuthorityEmbeddedSqlite")]
    [Arguments(PrivacyErasureAuthorityTopology.CoLocated, "AuthorityCoLocatedSqlite")]
    [TUnit.Core.Executors.TestExecutor<FreshEfProcessExecutor>]
    public async Task MigrateAndSeedAsync_SqliteAppliesExactlyOneAuthorityPath(
        PrivacyErasureAuthorityTopology topology,
        string authorityOperation)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"migrator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string primaryPath = Path.Combine(directory, "event.db");
        string embeddedPath = Path.Combine(directory, "authority.db");
        var migrationDatabase = SqliteOptions(primaryPath, PrimaryDatabaseRole.Migrator);
        var runtimeDatabase = SqliteOptions(primaryPath, PrimaryDatabaseRole.Runtime);
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["PrivacyErasure:Authority:Topology"] = topology.ToString(),
                ["PrivacyErasureAuthorityEmbedded:Path"] = embeddedPath,
            }).Build();
        var logger = new MigrationOperationLogger();

        try
        {
            await using ExploreDbContext runtime = CreateApplicationContext(runtimeDatabase);
            await PrepareCurrentApplicationStoreAsync(runtime);
            await ExploreDatabaseMigrator.MigrateAndSeedAsync(
                runtime,
                ProductionHostEnvironment.Instance,
                configuration,
                migrationDatabase,
                logger);

            string[] expectedOperations =
                ["Application", "ProviderAdjustments", "DataProtection", authorityOperation, "Seed"];
            await Assert.That(logger.Operations.Count).IsEqualTo(expectedOperations.Length);
            for (int index = 0; index < expectedOperations.Length; index++)
            {
                await Assert.That(logger.Operations[index]).IsEqualTo(expectedOperations[index]);
            }
            await AssertMigrationSetCompleteAsync(runtime);
            await Assert.That(await runtime.LocationKinds.AnyAsync()).IsTrue();
            await Assert.That(await ReadSqliteJournalModeAsync(runtime)).IsEqualTo("wal");

            await using DataProtectionKeyContext dataProtection = CreateDataProtectionContext(migrationDatabase);
            await AssertMigrationSetCompleteAsync(dataProtection);

            await using EmbeddedPrivacyErasureAuthorityDbContext selectedAuthority =
                CreateSqliteAuthorityContext(topology, migrationDatabase, embeddedPath);
            await AssertMigrationSetCompleteAsync(selectedAuthority);

            if (topology == PrivacyErasureAuthorityTopology.EmbeddedSqlite)
            {
                await using EmbeddedPrivacyErasureAuthorityDbContext unselectedCoLocated =
                    CreateSqliteAuthorityContext(PrivacyErasureAuthorityTopology.CoLocated, migrationDatabase, embeddedPath);
                await Assert.That(await unselectedCoLocated.Database.GetAppliedMigrationsAsync()).IsEmpty();
            }
            else
            {
                await Assert.That(File.Exists(embeddedPath)).IsFalse();
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static PrimaryDatabaseConnectionOptions SqliteOptions(
        string path,
        PrimaryDatabaseRole role) => new()
        {
            Role = role,
            Provider = PrimaryDatabaseProvider.Sqlite,
            Database = path,
        };

    private static ExploreDbContext CreateApplicationContext(PrimaryDatabaseConnectionOptions database)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, database);
        return new ExploreDbContext(options.Options);
    }

    private static DataProtectionKeyContext CreateDataProtectionContext(PrimaryDatabaseConnectionOptions database)
    {
        var options = TestDbContextOptions.Create<DataProtectionKeyContext>();
        PrimaryDatabaseProviderComposition.ConfigureDataProtection(options, database);
        return new DataProtectionKeyContext(options.Options);
    }

    private static EmbeddedPrivacyErasureAuthorityDbContext CreateSqliteAuthorityContext(
        PrivacyErasureAuthorityTopology topology,
        PrimaryDatabaseConnectionOptions database,
        string embeddedPath)
    {
        var options = TestDbContextOptions.Create<EmbeddedPrivacyErasureAuthorityDbContext>();
        if (topology == PrivacyErasureAuthorityTopology.CoLocated)
        {
            EmbeddedPrivacyErasureAuthorityDbContextFactory.ConfigureCoLocated(options, database);
        }
        else
        {
            EmbeddedPrivacyErasureAuthorityDbContextFactory.Configure(
                options,
                new EmbeddedPrivacyErasureAuthorityOptions { Path = embeddedPath });
        }

        return new EmbeddedPrivacyErasureAuthorityDbContext(options.Options);
    }

    internal static async Task AssertMigrationSetCompleteAsync(DbContext context)
    {
        string[] known = context.Database.GetMigrations().ToArray();
        string[] applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        await Assert.That(known).IsNotEmpty();
        await Assert.That(applied).IsEquivalentTo(known);
    }

    private static async Task PrepareCurrentApplicationStoreAsync(ExploreDbContext context)
    {
        await context.Database.EnsureCreatedAsync();
        IHistoryRepository history = context.GetService<IHistoryRepository>();
        await context.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript());
        foreach (string migration in context.Database.GetMigrations())
        {
            await context.Database.ExecuteSqlRawAsync(
                history.GetInsertScript(new HistoryRow(migration, ProductInfo.GetVersion())));
        }
    }

    private static async Task<string> ReadSqliteJournalModeAsync(DbContext context)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        return (string)(await command.ExecuteScalarAsync())!;
    }
}

public sealed class MigrationOperationLogger : ILogger
{
    public List<string> Operations { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (state is IEnumerable<KeyValuePair<string, object?>> properties
            && properties.FirstOrDefault(property => property.Key == "Operation").Value is string operation)
        {
            Operations.Add(operation);
        }
    }
}

public sealed class ProductionHostEnvironment : IHostEnvironment
{
    public static ProductionHostEnvironment Instance { get; } = new();

    public string EnvironmentName { get; set; } = Environments.Production;
    public string ApplicationName { get; set; } = "Event.Persistence.IntegrationTests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
