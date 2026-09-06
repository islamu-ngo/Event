// ABOUTME: Contract tests for structured primary database options and native builders.
// ABOUTME: Exercise runtime/migrator roles, validation matrices, redaction, and provider-specific connection-string output.

using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Bootstrap;
using Explore.Secrets.Database;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Npgsql;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Database;

public sealed class PrimaryDatabaseConfigurationTests
{
    private static IConfiguration BuildConfiguration(IDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Test]
    public async Task BindRuntime_StructuredSchemaOverridesEnvironmentAlias()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSql",
            ["Database:Host"] = "pg.example.test",
            ["Database:Database"] = "event_db",
            ["Database:Schema"] = "structured_event",
            ["DATABASE_SCHEMA"] = "alias_event",
            ["Database:Runtime:Username"] = "app_user",
            ["Database:Runtime:Password"] = "runtime-secret",
        });

        await Assert.That(PrimaryDatabaseConfiguration.BindRuntime(configuration).Schema).IsEqualTo("structured_event");
    }

    [Test]
    public async Task BindRuntime_UsesSchemaAliasAndDefault()
    {
        var values = new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSql",
            ["Database:Host"] = "pg.example.test",
            ["Database:Database"] = "event_db",
            ["Database:Runtime:Username"] = "app_user",
            ["Database:Runtime:Password"] = "runtime-secret",
        };

        await Assert.That(PrimaryDatabaseConfiguration.BindRuntime(BuildConfiguration(values)).Schema)
            .IsEqualTo(PrimaryDatabaseConnectionOptions.DefaultSchema);
        values["DATABASE_SCHEMA"] = "alias_event";
        await Assert.That(PrimaryDatabaseConfiguration.BindRuntime(BuildConfiguration(values)).Schema).IsEqualTo("alias_event");
    }

    [Test]
    public async Task BindRuntime_RejectsUnsupportedPrefixAlias()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSql",
            ["Database:Host"] = "pg.example.test",
            ["Database:Database"] = "event_db",
            ["DATABASE_PREFIX"] = "ie_custom",
            ["Database:Runtime:Username"] = "app_user",
            ["Database:Runtime:Password"] = "runtime-secret",
        });

        Action act = () => PrimaryDatabaseConfiguration.BindRuntime(configuration);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(act));
        await Assert.That(exception!.Message).Contains("Prefix overrides are not supported");
    }

    [Test]
    public async Task BindRuntime_RejectsRuntimePrefixEnvironmentAlias()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSql",
            ["Database:Host"] = "pg.example.test",
            ["Database:Database"] = "event_db",
            ["DATABASE_RUNTIME_PREFIX"] = "ie_custom",
            ["Database:Runtime:Username"] = "app_user",
            ["Database:Runtime:Password"] = "runtime-secret",
        });

        Action act = () => PrimaryDatabaseConfiguration.BindRuntime(configuration);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(act));
        await Assert.That(exception!.Message).Contains("Prefix overrides are not supported");
    }

    [Test]
    public async Task BindRuntime_RejectsStructuredPrefixAlias()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSql",
            ["Database:Host"] = "pg.example.test",
            ["Database:Database"] = "event_db",
            ["Database:Prefix"] = "ie_custom",
            ["Database:Runtime:Username"] = "app_user",
            ["Database:Runtime:Password"] = "runtime-secret",
        });

        Action act = () => PrimaryDatabaseConfiguration.BindRuntime(configuration);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(act));
        await Assert.That(exception!.Message).Contains("Prefix overrides are not supported");
    }

    [Test]
    public async Task BindRuntime_RejectsRuntimeStructuredPrefixAlias()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSql",
            ["Database:Host"] = "pg.example.test",
            ["Database:Database"] = "event_db",
            ["Database:Runtime:Prefix"] = "ie_custom",
            ["Database:Runtime:Username"] = "app_user",
            ["Database:Runtime:Password"] = "runtime-secret",
        });

        Action act = () => PrimaryDatabaseConfiguration.BindRuntime(configuration);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(act));
        await Assert.That(exception!.Message).Contains("Prefix overrides are not supported");
    }

    [Test]
    public async Task BindMigrator_RejectsUnsupportedPrefixAlias()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSql",
            ["Database:Host"] = "pg.example.test",
            ["Database:Database"] = "event_db",
            ["DATABASE_PREFIX"] = "ie_custom",
            ["Database:Migrator:Username"] = "migrator_user",
            ["Database:Migrator:Password"] = "migrator-secret",
        });

        Action act = () => PrimaryDatabaseConfiguration.BindMigrator(configuration);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(act));
        await Assert.That(exception!.Message).Contains("Prefix overrides are not supported");
    }

    [Test]
    public async Task BindMigrator_RejectsMigratorPrefixEnvironmentAlias()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSql",
            ["Database:Host"] = "pg.example.test",
            ["Database:Database"] = "event_db",
            ["DATABASE_MIGRATOR_PREFIX"] = "ie_custom",
            ["Database:Migrator:Username"] = "migrator_user",
            ["Database:Migrator:Password"] = "migrator-secret",
        });

        Action act = () => PrimaryDatabaseConfiguration.BindMigrator(configuration);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(act));
        await Assert.That(exception!.Message).Contains("Prefix overrides are not supported");
    }

    [Test]
    public async Task BindMigrator_RejectsStructuredPrefixAlias()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSql",
            ["Database:Host"] = "pg.example.test",
            ["Database:Database"] = "event_db",
            ["Database:Prefix"] = "ie_custom",
            ["Database:Migrator:Username"] = "migrator_user",
            ["Database:Migrator:Password"] = "migrator-secret",
        });

        Action act = () => PrimaryDatabaseConfiguration.BindMigrator(configuration);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(act));
        await Assert.That(exception!.Message).Contains("Prefix overrides are not supported");
    }

    [Test]
    public async Task BindMigrator_RejectsMigratorStructuredPrefixAlias()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSql",
            ["Database:Host"] = "pg.example.test",
            ["Database:Database"] = "event_db",
            ["Database:Migrator:Prefix"] = "ie_custom",
            ["Database:Migrator:Username"] = "migrator_user",
            ["Database:Migrator:Password"] = "migrator-secret",
        });

        Action act = () => PrimaryDatabaseConfiguration.BindMigrator(configuration);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(act));
        await Assert.That(exception!.Message).Contains("Prefix overrides are not supported");
    }

    [Test]
    public async Task BuildConnectionString_PostgreSqlCustomSchemaSetsSessionSearchPath()
    {
        var options = new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Runtime,
            Provider = PrimaryDatabaseProvider.PostgreSql,
            Host = "pg.example.test",
            Database = "event_db",
            Schema = "custom_event",
            Username = "app_user",
            Password = "runtime-secret",
            TlsMode = PrimaryDatabaseTlsMode.Disabled,
        };

        var result = PrimaryDatabaseConfiguration.BuildConnectionString(options);

        await Assert.That(new NpgsqlConnectionStringBuilder(result.ConnectionString).SearchPath)
            .IsEqualTo("custom_event");
    }

    [Test]
    public async Task BuildConnectionString_PostgreSqlMigratorSearchPathKeepsPublicBootstrapFallback()
    {
        var options = new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Migrator,
            Provider = PrimaryDatabaseProvider.PostgreSql,
            Host = "pg.example.test",
            Database = "event_db",
            Schema = "custom_event",
            Username = "migrator_user",
            Password = "migrator-secret",
            TlsMode = PrimaryDatabaseTlsMode.Disabled,
        };

        var result = PrimaryDatabaseConfiguration.BuildConnectionString(options);

        await Assert.That(new NpgsqlConnectionStringBuilder(result.ConnectionString).SearchPath)
            .IsEqualTo("custom_event, public");
    }

    [Test]
    [Arguments("bad-schema")]
    [Arguments("9bad")]
    [Arguments("schema;drop")]
    [Arguments("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task BindRuntime_RejectsUnsafeSchema(string schema)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSql",
            ["Database:Host"] = "pg.example.test",
            ["Database:Database"] = "event_db",
            ["Database:Schema"] = schema,
            ["Database:Runtime:Username"] = "app_user",
            ["Database:Runtime:Password"] = "runtime-secret",
        });

        var action = () => PrimaryDatabaseConfiguration.BindRuntime(configuration);
        await Assert.That(action).Throws<OptionsValidationException>();
    }

    [Test]
    public async Task BindRuntime_WithPostgreSqlAndSharedEndpoint_ComposesRuntimeOptions()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSql",
            ["Database:Host"] = "pg.example.test",
            ["Database:Database"] = "event_db",
            ["Database:Runtime:Username"] = "app_user",
            ["Database:Runtime:Password"] = "runtime-secret",
            ["Database:Runtime:TlsMode"] = "Required",
            ["Database:Runtime:TrustServerCertificate"] = "true",
        });

        var options = PrimaryDatabaseConfiguration.BindRuntime(configuration);
        var result = PrimaryDatabaseConfiguration.BuildConnectionString(options);

        await Assert.That(options.Role).IsEqualTo(PrimaryDatabaseRole.Runtime);
        await Assert.That(options.Provider).IsEqualTo(PrimaryDatabaseProvider.PostgreSql);
        await Assert.That(options.Port).IsNull();
        await Assert.That(options.TlsMode).IsEqualTo(PrimaryDatabaseTlsMode.Required);
        await Assert.That(options.TrustServerCertificate).IsTrue();

        var parsed = new NpgsqlConnectionStringBuilder(result.ConnectionString);
        await Assert.That(parsed.Host).IsEqualTo("pg.example.test");
        await Assert.That(parsed.Port).IsEqualTo(5432);
        await Assert.That(parsed.Database).IsEqualTo("event_db");
        await Assert.That(parsed.Username).IsEqualTo("app_user");
        await Assert.That(parsed.Password).IsEqualTo("runtime-secret");
        await Assert.That(parsed.SslMode).IsEqualTo(SslMode.Require);
        await Assert.That(result.RedactedConnectionString).DoesNotContain("runtime-secret");
        await Assert.That(result.SafeSummary).Contains("Runtime:PostgreSql");
        await Assert.That(result.SafeSummary).Contains("tls=Required");
    }

    [Test]
    public async Task BindMigrator_WithDistinctCredentials_ComposesMigratorOptions()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSql",
            ["Database:Host"] = "pg.example.test",
            ["Database:Database"] = "event_db",
            ["Database:Migrator:Username"] = "migrator_user",
            ["Database:Migrator:Password"] = "migrator-secret",
        });

        var options = PrimaryDatabaseConfiguration.BindMigrator(configuration);
        var result = PrimaryDatabaseConfiguration.BuildConnectionString(options);

        await Assert.That(options.Role).IsEqualTo(PrimaryDatabaseRole.Migrator);
        await Assert.That(new NpgsqlConnectionStringBuilder(result.ConnectionString).Username).IsEqualTo("migrator_user");
        await Assert.That(result.RedactedConnectionString).DoesNotContain("migrator-secret");
    }

    [Test]
    public async Task BindRuntime_WithSqlite_UsesLocalFilePath()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:Database"] = "/tmp/event-primary.db",
        });

        var options = PrimaryDatabaseConfiguration.BindRuntime(configuration);
        var result = PrimaryDatabaseConfiguration.BuildConnectionString(options);

        await Assert.That(options.Provider).IsEqualTo(PrimaryDatabaseProvider.Sqlite);
        await Assert.That(options.Role).IsEqualTo(PrimaryDatabaseRole.Runtime);
        await Assert.That(options.Database).IsEqualTo("/tmp/event-primary.db");

        var parsed = new SqliteConnectionStringBuilder(result.ConnectionString);
        await Assert.That(parsed.DataSource).IsEqualTo("/tmp/event-primary.db");
        await Assert.That(parsed.Mode).IsEqualTo(SqliteOpenMode.ReadWriteCreate);
        await Assert.That(parsed.DefaultTimeout).IsEqualTo(30);
        await Assert.That(result.RedactedConnectionString).DoesNotContain("runtime-secret");
    }

    [Test]
    public async Task BindRuntime_WithSqlServer_UsesDefaultPortAndNativeBuilder()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "SqlServer",
            ["Database:Host"] = "sql.example.test",
            ["Database:Database"] = "event_db",
            ["Database:Runtime:Username"] = "sql_user",
            ["Database:Runtime:Password"] = "sql-secret",
            ["Database:Runtime:TlsMode"] = "Required",
        });

        var result = PrimaryDatabaseConfiguration.BuildConnectionString(PrimaryDatabaseConfiguration.BindRuntime(configuration));
        var parsed = new SqlConnectionStringBuilder(result.ConnectionString);

        await Assert.That(parsed.ConnectionString).Contains("Data Source=sql.example.test");
        await Assert.That(parsed.InitialCatalog).IsEqualTo("event_db");
        await Assert.That(parsed.UserID).IsEqualTo("sql_user");
        await Assert.That(parsed.Password).IsEqualTo("sql-secret");
        await Assert.That(parsed.ConnectionString).Contains("Encrypt=True");
        await Assert.That(result.SafeSummary).Contains("port=1433");
        await Assert.That(result.RedactedConnectionString).DoesNotContain("sql-secret");
    }

    [Test]
    public async Task BindRuntime_WithMariaDb_RequiresFlavorAndVersion()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "MariaDb",
            ["Database:Host"] = "mariadb.example.test",
            ["Database:Database"] = "event_db",
            ["Database:Runtime:Username"] = "db_user",
            ["Database:Runtime:Password"] = "db-secret",
            ["Database:Runtime:ServerFlavor"] = "MariaDb",
            ["Database:Runtime:ServerVersion"] = "10.11",
            ["Database:Runtime:TlsMode"] = "Required",
        });

        var options = PrimaryDatabaseConfiguration.BindRuntime(configuration);
        var result = PrimaryDatabaseConfiguration.BuildConnectionString(options);
        var parsed = new MySqlConnectionStringBuilder(result.ConnectionString);

        await Assert.That(options.ServerFlavor).IsEqualTo(PrimaryDatabaseServerFlavor.MariaDb);
        await Assert.That(options.ServerVersion).IsEqualTo(new Version(10, 11));
        await Assert.That(parsed.Server).IsEqualTo("mariadb.example.test");
        await Assert.That(parsed.Port).IsEqualTo(3306u);
        await Assert.That(parsed.UserID).IsEqualTo("db_user");
        await Assert.That(parsed.Password).IsEqualTo("db-secret");
        await Assert.That(result.SafeSummary).Contains("MariaDb");
        await Assert.That(result.SafeSummary).Contains("port=3306");
    }

    [Test]
    public async Task BindRuntime_WithMariaDb_AutoInfereFlavorAndDefaultLtsVersion()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "MariaDb",
            ["Database:Host"] = "mariadb.example.test",
            ["Database:Database"] = "event_db",
            ["Database:Runtime:Username"] = "db_user",
            ["Database:Runtime:Password"] = "db-secret",
            ["Database:Runtime:TlsMode"] = "Required",
        });

        var options = PrimaryDatabaseConfiguration.BindRuntime(configuration);
        await Assert.That(options.ServerFlavor).IsEqualTo(PrimaryDatabaseServerFlavor.MariaDb);
        await Assert.That(options.ServerVersion).IsEqualTo(new Version(11, 4));
    }

    [Test]
    public async Task BindRuntime_WithMySql_AutoInfereFlavorAndDefaultLtsVersion()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "MySql",
            ["Database:Host"] = "mysql.example.test",
            ["Database:Database"] = "event_db",
            ["Database:Runtime:Username"] = "db_user",
            ["Database:Runtime:Password"] = "db-secret",
            ["Database:Runtime:TlsMode"] = "Prefer",
        });

        var options = PrimaryDatabaseConfiguration.BindRuntime(configuration);
        await Assert.That(options.ServerFlavor).IsEqualTo(PrimaryDatabaseServerFlavor.MySql);
        await Assert.That(options.ServerVersion).IsEqualTo(new Version(8, 4));
    }

    [Test]
    public async Task Validate_RejectsSqliteWithServerFields()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:Database"] = "/tmp/event.db",
            ["Database:Runtime:Host"] = "not-allowed",
            ["Database:Runtime:Username"] = "not-allowed",
            ["Database:Runtime:Password"] = "not-allowed",
        });

        Action act = () => PrimaryDatabaseConfiguration.BindRuntime(configuration);

        await Assert.That(act).Throws<OptionsValidationException>();
    }

    [Test]
    public async Task Validate_RejectsUnknownProvider()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Oracle",
        });

        Action act = () => PrimaryDatabaseConfiguration.BindRuntime(configuration);

        await Assert.That(act).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("1")]
    [Arguments("999")]
    public async Task BindRuntime_RejectsNumericProviderValues(string provider)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = provider,
        });

        Action act = () => PrimaryDatabaseConfiguration.BindRuntime(configuration);

        await Assert.That(act).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("1")]
    [Arguments("999")]
    public async Task BindRuntime_RejectsNumericTlsModeValues(string tlsMode)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSql",
            ["Database:Host"] = "pg.example.test",
            ["Database:Database"] = "event_db",
            ["Database:Runtime:Username"] = "app_user",
            ["Database:Runtime:Password"] = "app-secret",
            ["Database:Runtime:TlsMode"] = tlsMode,
        });

        Action act = () => PrimaryDatabaseConfiguration.BindRuntime(configuration);

        await Assert.That(act).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("1")]
    [Arguments("999")]
    public async Task BindRuntime_RejectsNumericServerFlavorValues(string serverFlavor)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "MySql",
            ["Database:Host"] = "mysql.example.test",
            ["Database:Database"] = "event_db",
            ["Database:Runtime:Username"] = "app_user",
            ["Database:Runtime:Password"] = "app-secret",
            ["Database:Runtime:ServerFlavor"] = serverFlavor,
            ["Database:Runtime:ServerVersion"] = "8.0",
        });

        Action act = () => PrimaryDatabaseConfiguration.BindRuntime(configuration);

        await Assert.That(act).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(":memory:")]
    [Arguments("file::memory:")]
    [Arguments("file::memory:?cache=shared")]
    [Arguments("file:shared?mode=memory&cache=shared")]
    [Arguments("file:shared?cache=shared&mode=memory")]
    public async Task BindRuntime_RejectsNonPersistedSqliteDatabase(string database)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:Database"] = database,
        });

        Action act = () => PrimaryDatabaseConfiguration.BindRuntime(configuration);

        await Assert.That(act).Throws<OptionsValidationException>();
    }

    [Test]
    [Arguments("//database-server/event.db")]
    [Arguments("\\\\database-server\\share\\event.db")]
    [Arguments("file:///app/data/event.db")]
    [Arguments("/app/data/privacy_erasure_authority.db")]
    [Arguments("privacy_erasure_authority.db")]
    public async Task BindRuntime_RejectsNonLocalOrReservedSqliteDatabase(string database)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:Database"] = database,
        });

        Action act = () => PrimaryDatabaseConfiguration.BindRuntime(configuration);

        await Assert.That(act).Throws<OptionsValidationException>();
    }

    [Test]
    public async Task SqliteInitializer_PersistsWalAndConfiguredBusyTimeoutAcrossReopen()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"event-primary-{Guid.NewGuid():N}.db");
        var result = PrimaryDatabaseConfiguration.BuildConnectionString(new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Runtime,
            Provider = PrimaryDatabaseProvider.Sqlite,
            Database = databasePath,
        });

        try
        {
            var options = TestDbContextOptions.Create();
            options.UseSqlite(result.ConnectionString);
            await using (var context = new DbContext(options.Options))
            {
                await SqliteDatabaseInitializer.InitializeAsync(context, CancellationToken.None);
            }

            await using var connection = new SqliteConnection(result.ConnectionString);
            await connection.OpenAsync(CancellationToken.None);
            await using var command = connection.CreateCommand();
            await Assert.That(command.CommandTimeout).IsEqualTo(30);
            command.CommandText = "PRAGMA journal_mode;";
            await Assert.That(await command.ExecuteScalarAsync(CancellationToken.None)).IsEqualTo("wal");
        }
        finally
        {
            File.Delete(databasePath);
            File.Delete($"{databasePath}-shm");
            File.Delete($"{databasePath}-wal");
        }
    }

    [Test]
    public async Task SqliteInitializer_WithNonSqliteProvider_DoesNotExecuteSqliteSql()
    {
        await using var context = new DbContext(TestDbContextOptions.Create()
            .UseTestInMemoryDatabase($"sqlite-initializer-{Guid.NewGuid():N}")
            .Options);

        var act = () => SqliteDatabaseInitializer.InitializeAsync(context, CancellationToken.None);

        await Assert.That(act).ThrowsNothing();
    }

    [Test]
    public async Task BuildConnectionString_PostgreSqlRequiredTlsWithoutTrustBypass_VerifiesServerIdentity()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSql",
            ["Database:Host"] = "pg.example.test",
            ["Database:Database"] = "event_db",
            ["Database:Runtime:Username"] = "app_user",
            ["Database:Runtime:Password"] = "app-secret",
            ["Database:Runtime:TlsMode"] = "Required",
        });

        var result = PrimaryDatabaseConfiguration.ResolveRuntimeConnectionString(configuration);

        await Assert.That(new NpgsqlConnectionStringBuilder(result.ConnectionString).SslMode).IsEqualTo(SslMode.VerifyFull);
    }

    [Test]
    public async Task BuildConnectionString_PostgreSqlDefaultTls_VerifiesServerIdentity()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSql",
            ["Database:Host"] = "pg.example.test",
            ["Database:Database"] = "event_db",
            ["Database:Runtime:Username"] = "app_user",
            ["Database:Runtime:Password"] = "app-secret",
        });

        var result = PrimaryDatabaseConfiguration.ResolveRuntimeConnectionString(configuration);

        await Assert.That(new NpgsqlConnectionStringBuilder(result.ConnectionString).SslMode).IsEqualTo(SslMode.VerifyFull);
    }

    [Test]
    public async Task BuildConnectionString_PostgreSqlRequiredTlsWithExplicitTrustBypass_RequiresEncryptionWithoutVerification()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSql",
            ["Database:Host"] = "pg.example.test",
            ["Database:Database"] = "event_db",
            ["Database:Runtime:Username"] = "app_user",
            ["Database:Runtime:Password"] = "app-secret",
            ["Database:Runtime:TlsMode"] = "Required",
            ["Database:Runtime:TrustServerCertificate"] = "true",
        });

        var result = PrimaryDatabaseConfiguration.ResolveRuntimeConnectionString(configuration);

        await Assert.That(new NpgsqlConnectionStringBuilder(result.ConnectionString).SslMode).IsEqualTo(SslMode.Require);
    }

    [Test]
    public async Task BindRuntime_RejectsTrustBypassWithoutRequiredTls()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSql",
            ["Database:Host"] = "pg.example.test",
            ["Database:Database"] = "event_db",
            ["Database:Runtime:Username"] = "app_user",
            ["Database:Runtime:Password"] = "app-secret",
            ["Database:Runtime:TlsMode"] = "Prefer",
            ["Database:Runtime:TrustServerCertificate"] = "true",
        });

        Action act = () => PrimaryDatabaseConfiguration.BindRuntime(configuration);

        await Assert.That(act).Throws<OptionsValidationException>();
    }

    [Test]
    public async Task Validate_RejectsSqlServerMissingDatabase()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "SqlServer",
            ["Database:Host"] = "sql.example.test",
            ["Database:Runtime:Username"] = "sql_user",
            ["Database:Runtime:Password"] = "sql-secret",
        });

        Action act = () => PrimaryDatabaseConfiguration.BindRuntime(configuration);

        await Assert.That(act).Throws<OptionsValidationException>();
    }

    [Test]
    [Arguments("Development")]
    [Arguments("Testing")]
    [Arguments(null)]
    [Arguments("Production")]
    public async Task ResolveRuntimeConnectionString_WithLegacyConnectionString_RejectsEveryEnvironment(
        string? environmentName)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=legacy;Username=postgres;Password=secret",
            ["ASPNETCORE_ENVIRONMENT"] = environmentName,
        });

        Action act = () => PrimaryDatabaseConfiguration.ResolveRuntimeConnectionString(configuration);

        await Assert.That(act).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ConfigurePersistenceServices_WhenDatabaseRegistrationIsSkipped_DoesNotRequireDatabaseConfiguration()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>());
        var services = new ServiceCollection();

        Action act = () => services.ConfigurePersistenceServices(
            configuration,
            skipDbContextRegistration: true,
            skipLookupCacheInitializer: true,
            environmentName: "Production");

        await Assert.That(act).ThrowsNothing();
    }

    [Test]
    public async Task ConfigurePersistenceServices_WhenDatabaseRegistrationIsActive_RequiresStructuredConfiguration()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>());
        var services = new ServiceCollection();

        Action act = () => services.ConfigurePersistenceServices(
            configuration,
            skipDbContextRegistration: false,
            skipLookupCacheInitializer: true,
            environmentName: "Production");

        await Assert.That(act).Throws<InvalidOperationException>();
    }

    [Test]
    [NotInParallel]
    public async Task ConfigurePersistenceServices_WithProjectedDiscretePostgres_UsesSharedStructuredBinder()
    {
        string password = $"password-{Guid.CreateVersion7():N}";
        var values = new Dictionary<string, string?>
        {
            [BootstrapSecretLoader.EnvHost] = "pg.example.test",
            [BootstrapSecretLoader.EnvPort] = "5432",
            [BootstrapSecretLoader.EnvDatabase] = "event_db",
            [BootstrapSecretLoader.EnvUsername] = $"user_{Guid.CreateVersion7():N}",
            [BootstrapSecretLoader.EnvPassword] = password,
        };
        var previous = values.Keys.ToDictionary(
            key => key,
            Environment.GetEnvironmentVariable,
            StringComparer.Ordinal);
        try
        {
            foreach (var pair in values)
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);

            var builder = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecretProvider:Provider"] = "Environment",
            });
            BootstrapSecretLoader.ProjectPostgresConfiguration(
                builder,
                PrimaryDatabaseRole.Runtime);
            var services = new ServiceCollection();

            Action act = () => services.ConfigurePersistenceServices(
                builder.Build(),
                skipDbContextRegistration: false,
                skipLookupCacheInitializer: true,
                environmentName: "Production");

            await Assert.That(act).ThrowsNothing();
        }
        finally
        {
            foreach (var pair in previous)
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }

    [Test]
    [NotInParallel]
    public async Task DesignTimeFactory_WhenUserSecretsIsSelectedInProduction_FailsClosed()
    {
        const string providerKey = "SecretProvider__Provider";
        const string environmentKey = "DOTNET_ENVIRONMENT";
        string? previousProvider = Environment.GetEnvironmentVariable(providerKey);
        string? previousEnvironment = Environment.GetEnvironmentVariable(environmentKey);
        try
        {
            Environment.SetEnvironmentVariable(providerKey, "UserSecrets");
            Environment.SetEnvironmentVariable(environmentKey, "Production");

            Action act = () => new ExploreDbContextFactory().CreateDbContext([]);

            var exception = await Assert.That(act).Throws<InvalidOperationException>();
            await Assert.That(exception!.Message)
                .IsEqualTo("secret_authority_user_secrets_environment_invalid");
        }
        finally
        {
            Environment.SetEnvironmentVariable(providerKey, previousProvider);
            Environment.SetEnvironmentVariable(environmentKey, previousEnvironment);
        }
    }

    [Test]
    public async Task BuildConnectionString_RedactsSensitiveValues()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSql",
            ["Database:Host"] = "pg.example.test",
            ["Database:Database"] = "event_db",
            ["Database:Runtime:Username"] = "app_user",
            ["Database:Runtime:Password"] = "MDB_SECRET_SENTINEL",
        });

        var result = PrimaryDatabaseConfiguration.BuildConnectionString(PrimaryDatabaseConfiguration.BindRuntime(configuration));

        await Assert.That(result.RedactedConnectionString).DoesNotContain("MDB_SECRET_SENTINEL");
        await Assert.That(result.SafeSummary).DoesNotContain("MDB_SECRET_SENTINEL");
    }
}
