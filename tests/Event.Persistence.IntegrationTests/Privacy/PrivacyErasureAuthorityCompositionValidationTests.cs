using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Configuration;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Privacy.ErasureAuthority.Repositories;
using Explore.Secrets.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Privacy;

[ClassDataSource<RecipientDeliveryMigrationContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("RecipientDeliveryMigrationDb")]
public sealed class PrivacyErasureAuthorityCompositionValidationTests(
    RecipientDeliveryMigrationContainerFixture fixture)
{
    [Test]
    public async Task DefaultComposition_RegistersEmbeddedAuthorityWithoutExternalDatabase()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddOptions<PrivacyErasureOptions>();
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["PrivacyErasureAuthorityEmbedded:Path"] =
                    Path.Combine(Path.GetTempPath(), $"authority-{Guid.CreateVersion7():N}.db"),
                ["Database:Provider"] = "PostgreSql",
                ["Database:Host"] = "unused",
                ["Database:Database"] = "unused",
                ["Database:Runtime:Username"] = "unused",
                ["Database:Runtime:Password"] = "unused"
            }).Build();

        services.ConfigurePersistenceServices(
            configuration,
            skipLookupCacheInitializer: true);

        await using ServiceProvider provider = services.BuildIsolatedServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IPrivacyErasureAuthority authority =
            scope.ServiceProvider.GetRequiredService<IPrivacyErasureAuthority>();

        await Assert.That(authority)
            .IsTypeOf<EmbeddedPrivacyErasureAuthorityRepository>();
        await Assert.That(services.Any(descriptor =>
            descriptor.ServiceType == typeof(IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>)))
            .IsTrue();
        await Assert.That(services.Any(descriptor =>
            descriptor.ServiceType == typeof(PrivacyErasureAuthorityDbContext))).IsFalse();
    }

    [Test]
    public async Task CoLocatedPostgresTopology_RegistersPrimaryDatabaseAuthorityAdapter()
    {
        var settings = PrimaryDatabaseSettings(new NpgsqlConnectionStringBuilder
        {
            Host = "localhost",
            Database = "event",
            Username = "application",
            Password = "application-canary"
        });
        settings["PrivacyErasure:Authority:Topology"] = "CoLocated";
        settings["PrivacyErasureAuthorityEmbedded:Path"] =
            Path.Combine(Path.GetTempPath(), $"authority-{Guid.CreateVersion7():N}.db");
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.ConfigurePersistenceServices(
            configuration,
            skipDbContextRegistration: true,
            skipLookupCacheInitializer: true);
        services.ConfigureDbContext<CoLocatedPrivacyErasureAuthorityDbContext>(
            TestDbContextOptions.Apply);
        await using ServiceProvider provider = services.BuildIsolatedServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        await using CoLocatedPrivacyErasureAuthorityDbContext context = scope.ServiceProvider
            .GetRequiredService<CoLocatedPrivacyErasureAuthorityDbContext>();

        await Assert.That(context).IsNotNull();
        await Assert.That(services.Any(descriptor =>
            descriptor.ServiceType == typeof(IPrivacyErasureAuthority))).IsTrue();
        await Assert.That(services.Any(descriptor =>
            descriptor.ServiceType == typeof(EmbeddedPrivacyErasureAuthorityDbContext))).IsFalse();
        await Assert.That(services.Any(descriptor =>
            descriptor.ServiceType == typeof(EmbeddedPrivacyErasureAuthorityStorage))).IsFalse();
        await Assert.That(services.Any(descriptor =>
            descriptor.ServiceType == typeof(PrivacyErasureAuthorityDbContext))).IsFalse();
    }

    [Test]
    [Category("Runtime")]
    [Timeout(240_000)]
    public async Task CoLocatedPostgresTopology_MigratesAndAppendsInPrimarySchema()
    {
        await fixture.ResetAsync();

        const string schema = "custom_event";
        await using (var migrator = new CoLocatedPrivacyErasureAuthorityDbContext(
            CreateAuthorityOptions(PrimaryDatabaseRole.Migrator, schema)))
        {
            string migration = migrator.Database.GetMigrations().Single();
            IMigrator migrationRunner = migrator.GetService<IMigrator>();
            await migrationRunner.MigrateAsync(migration);
            await migrationRunner.MigrateAsync(Migration.InitialDatabase);
            await migrationRunner.MigrateAsync(migration);
            await migrationRunner.MigrateAsync(migration);
        }

        DbContextOptions<CoLocatedPrivacyErasureAuthorityDbContext> runtimeOptions =
            CreateAuthorityOptions(PrimaryDatabaseRole.Runtime, schema);
        await using var context = new CoLocatedPrivacyErasureAuthorityDbContext(runtimeOptions);
        var authority = new CoLocatedPostgresPrivacyErasureAuthorityRepository(
            context,
            TimeProvider.System,
            Options.Create(new PrivacyErasureOptions()));
        var request = new PrivacyErasureRequest(
            Guid.CreateVersion7(),
            PrivacyErasureSubjectKind.User,
            Guid.CreateVersion7(),
            PrivacyErasureReasonCode.SubjectErasureRequest,
            1);

        PrivacyErasureIntent appended = await authority.AppendAsync(request);
        PrivacyErasureIntent duplicate = await authority.AppendAsync(request);
        IReadOnlyList<PrivacyErasureIntent> replay = await authority.ReadAfterAsync(0, 10);

        await Assert.That(appended.AuthoritySequence).IsEqualTo(1);
        await Assert.That(duplicate.AuthoritySequence).IsEqualTo(appended.AuthoritySequence);
        await Assert.That(replay.Select(item => item.IntentId)).IsEquivalentTo([request.IntentId]);
        await Assert.That(context.Model.FindEntityType(typeof(PrivacyErasureIntent))!.GetSchema())
            .IsEqualTo(schema);

        await context.Database.ExecuteSqlRawAsync(
            "UPDATE \"custom_event\".\"erasure_intents\" "
            + "SET requested_at_utc = {0}, recorded_at_utc = {0}, retention_expires_at_utc = {1} "
            + "WHERE authority_sequence = {2}",
            DateTime.UtcNow.AddDays(-2),
            DateTime.UtcNow.AddDays(-1),
            appended.AuthoritySequence);

        await using var appendContext = new CoLocatedPrivacyErasureAuthorityDbContext(runtimeOptions);
        await using var compactContext = new CoLocatedPrivacyErasureAuthorityDbContext(runtimeOptions);
        var appendAuthority = new CoLocatedPostgresPrivacyErasureAuthorityRepository(
            appendContext,
            TimeProvider.System,
            Options.Create(new PrivacyErasureOptions()));
        var compactAuthority = new CoLocatedPostgresPrivacyErasureAuthorityRepository(
            compactContext,
            TimeProvider.System,
            Options.Create(new PrivacyErasureOptions()));
        Task<PrivacyErasureIntent> appendTask = appendAuthority.AppendAsync(
            new PrivacyErasureRequest(
                Guid.CreateVersion7(),
                PrivacyErasureSubjectKind.User,
                Guid.CreateVersion7(),
                PrivacyErasureReasonCode.SubjectErasureRequest,
                1));
        Task<PrivacyErasureCompactionResult> compactTask = compactAuthority
            .CompactExpiredIntentsAsync(new PrivacyErasureRetentionRequest(
                DateTime.UtcNow,
                100,
                []));

        await Task.WhenAll(appendTask, compactTask);

        PrivacyErasureIntent concurrent = await appendTask;
        PrivacyErasureAuthorityState state = await compactAuthority.GetStateAsync();
        await Assert.That(concurrent.AuthoritySequence).IsEqualTo(2);
        await Assert.That(state).IsEqualTo(new PrivacyErasureAuthorityState(2, 1));
        await compactContext.ErasureIntents
            .Where(fact => fact.AuthoritySequence == concurrent.AuthoritySequence)
            .ExecuteDeleteAsync();

        var gapRequest = new PrivacyErasureRetentionRequest(DateTime.UtcNow, 100, []);
        await Assert.ThrowsAsync<Explore.Application.Exceptions.PrivacyErasureSequenceGapException>(
            () => compactAuthority.EvaluateRetentionAsync(gapRequest));
        await Assert.ThrowsAsync<Explore.Application.Exceptions.PrivacyErasureSequenceGapException>(
            () => compactAuthority.CompactExpiredIntentsAsync(gapRequest));
        await Assert.That(await compactAuthority.GetStateAsync())
            .IsEqualTo(new PrivacyErasureAuthorityState(2, 1));
    }

    [Test]
    [Category("Runtime")]
    [Timeout(240_000)]
    public async Task CoLocatedPostgresTopologies_KeepMigratedAuthorityDataIsolatedAcrossSchemas()
    {
        await fixture.ResetAsync();
        var schemas = Enumerable.Range(0, 24)
            .Select(index => $"authority_isolation_{index}")
            .ToArray();
        var requests = schemas.Select(_ => PrivacyErasureRequest.Create(
            Guid.CreateVersion7(),
            PrivacyErasureSubjectKind.User,
            Guid.CreateVersion7(),
            PrivacyErasureReasonCode.SubjectErasureRequest,
            1)).ToArray();
        var retainedIds = new Guid[schemas.Length];

        for (var index = 0; index < schemas.Length; index++)
        {
            await using (var migrator = new CoLocatedPrivacyErasureAuthorityDbContext(
                CreateAuthorityOptions(PrimaryDatabaseRole.Migrator, schemas[index])))
            {
                await migrator.Database.MigrateAsync();
            }

            await using var context = new CoLocatedPrivacyErasureAuthorityDbContext(
                CreateAuthorityOptions(PrimaryDatabaseRole.Runtime, schemas[index]));
            var authority = new CoLocatedPostgresPrivacyErasureAuthorityRepository(
                context, TimeProvider.System, Options.Create(new PrivacyErasureOptions()));
            PrivacyErasureIntent retained = await authority.AppendAsync(requests[index]);
            retainedIds[index] = retained.IntentId;
            await Assert.That(retained.AuthoritySequence).IsEqualTo(1);
        }

        for (var index = 0; index < schemas.Length; index++)
        {
            await using var context = new CoLocatedPrivacyErasureAuthorityDbContext(
                CreateAuthorityOptions(PrimaryDatabaseRole.Runtime, schemas[index]));
            var authority = new CoLocatedPostgresPrivacyErasureAuthorityRepository(
                context, TimeProvider.System, Options.Create(new PrivacyErasureOptions()));
            IReadOnlyList<PrivacyErasureIntent> replay = await authority.ReadAfterAsync(0, 10);
            await Assert.That(replay.Select(intent => intent.IntentId))
                .IsEquivalentTo(new[] { retainedIds[index] });
        }
    }

    private DbContextOptions<CoLocatedPrivacyErasureAuthorityDbContext> CreateAuthorityOptions(
        PrimaryDatabaseRole role,
        string schema)
    {
        var options = TestDbContextOptions.Create<CoLocatedPrivacyErasureAuthorityDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureCoLocatedPrivacyErasureAuthority(
            options,
            CreatePostgresOptions(fixture.ConnectionString, role, schema));
        return options.Options;
    }

    [Test]
    public async Task CoLocatedSqliteTopology_UsesPrimaryFileAndFixedPrefixWithoutEmbeddedStorage()
    {
        string primaryPath = Path.Combine(
            Path.GetTempPath(),
            $"event-primary-{Guid.CreateVersion7():N}.db");
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["Database:Database"] = primaryPath,
                ["PrivacyErasure:Authority:Topology"] = "CoLocated"
            }).Build();
        var services = new ServiceCollection();
        services.AddOptions<PrivacyErasureOptions>();

        services.ConfigurePersistenceServices(
            configuration,
            skipDbContextRegistration: true,
            skipLookupCacheInitializer: true);

        services.ConfigureDbContext<EmbeddedPrivacyErasureAuthorityDbContext>(
            TestDbContextOptions.Apply,
            ServiceLifetime.Singleton);
        try
        {
            await using ServiceProvider provider = services.BuildIsolatedServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true });
            await using AsyncServiceScope scope = provider.CreateAsyncScope();
            IPrivacyErasureAuthority authority =
                scope.ServiceProvider.GetRequiredService<IPrivacyErasureAuthority>();
            var factory = scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>>();
            await using EmbeddedPrivacyErasureAuthorityDbContext context =
                await factory.CreateDbContextAsync();
            string migration = context.Database.GetMigrations().Single();
            IMigrator migrationRunner = context.GetService<IMigrator>();
            await migrationRunner.MigrateAsync(migration);
            await migrationRunner.MigrateAsync(Migration.InitialDatabase);
            await migrationRunner.MigrateAsync(migration);
            await migrationRunner.MigrateAsync(migration);
            string dataSource = new SqliteConnectionStringBuilder(
                context.Database.GetConnectionString()).DataSource;
            var request = PrivacyErasureRequest.Create(
                Guid.CreateVersion7(),
                PrivacyErasureSubjectKind.User,
                Guid.CreateVersion7(),
                PrivacyErasureReasonCode.SubjectErasureRequest,
                1);
            PrivacyErasureIntent retained = await authority.AppendAsync(request);
            IReadOnlyList<PrivacyErasureIntent> replay = await authority.ReadAfterAsync(0, 10);

            await Assert.That(authority).IsTypeOf<EmbeddedPrivacyErasureAuthorityRepository>();
            await Assert.That(services.Single(descriptor =>
                descriptor.ServiceType == typeof(IPrivacyErasureAuthority)).Lifetime)
                .IsEqualTo(ServiceLifetime.Singleton);
            await Assert.That(Path.GetFullPath(dataSource)).IsEqualTo(Path.GetFullPath(primaryPath));
            await Assert.That(context.Model.FindEntityType(typeof(PrivacyErasureIntent))!
                .GetTableName()).IsEqualTo("ie_erasure_intents");
            await Assert.That(replay.Select(item => item.IntentId)).Contains(retained.IntentId);
            await Assert.That(services.Any(descriptor =>
                descriptor.ServiceType == typeof(EmbeddedPrivacyErasureAuthorityStorage))).IsFalse();
            await Assert.That(services.Any(descriptor =>
                descriptor.ServiceType == typeof(PrivacyErasureAuthorityDbContext))).IsFalse();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(primaryPath);
            File.Delete(primaryPath + "-wal");
            File.Delete(primaryPath + "-shm");
        }
    }

    [Test]
    [Arguments("Port", "invalid")]
    [Arguments("TlsMode", "invalid")]
    public async Task ExternalComposition_InvalidStructuredValue_FailsBeforeRegistration(
        string field,
        string value)
    {
        var services = new ServiceCollection();
        var settings = AuthorityDatabaseSettings("authority", "privacy_erasure", "runtime", "secret");
        settings["PrivacyErasure:Authority:Topology"] = "ExternalDatabase";
        settings[$"PrivacyErasureAuthorityDatabase:{field}"] = value;
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        OptionsValidationException? exception = await Assert.That(() =>
                services.ConfigurePersistenceServices(
                    configuration,
                    skipDbContextRegistration: true,
                    skipLookupCacheInitializer: true))
            .Throws<OptionsValidationException>();

        await Assert.That(exception!.Message).DoesNotContain("secret");
        await Assert.That(services.Any(descriptor =>
            descriptor.ServiceType == typeof(PrivacyErasureAuthorityDbContext))).IsFalse();
        await Assert.That(services.Any(descriptor =>
            descriptor.ServiceType == typeof(IPrivacyErasureAuthority))).IsFalse();
    }

    [Test]
    public async Task ExternalComposition_SamePhysicalApplicationDatabase_FailsBeforeRegistration()
    {
        var applicationTarget = new NpgsqlConnectionStringBuilder
        {
            Host = "localhost",
            Database = "event",
            Username = "application",
            Password = "application-canary"
        };
        var services = new ServiceCollection();
        var settings = PrimaryDatabaseSettings(applicationTarget);
        settings["PrivacyErasure:Authority:Topology"] = "ExternalDatabase";
        foreach (var pair in AuthorityDatabaseSettings(
                     "127.0.0.1",
                     "event",
                     "runtime",
                     "authority-canary"))
        {
            settings[pair.Key] = pair.Value;
        }
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        OptionsValidationException? exception = await Assert.That(() =>
                services.ConfigurePersistenceServices(
                    configuration,
                    skipLookupCacheInitializer: true))
            .Throws<OptionsValidationException>();

        await Assert.That(exception!.Message)
            .Contains("different physical PostgreSQL database", StringComparison.OrdinalIgnoreCase);
        await Assert.That(exception.Message).DoesNotContain("application-canary");
        await Assert.That(exception.Message).DoesNotContain("authority-canary");
        await Assert.That(services.Any(descriptor =>
            descriptor.ServiceType == typeof(PrivacyErasureAuthorityDbContext))).IsFalse();
        await Assert.That(services.Any(descriptor =>
            descriptor.ServiceType == typeof(IPrivacyErasureAuthority))).IsFalse();
    }

    [Test]
    [Arguments(PrimaryDatabaseProvider.SqlServer, 1433)]
    [Arguments(PrimaryDatabaseProvider.MariaDb, 3306)]
    [Arguments(PrimaryDatabaseProvider.MySql, 3306)]
    public async Task CoLocatedUnsupportedPrimaryProvider_FailsClosedBeforeAuthorityAdapterRegistration(
        PrimaryDatabaseProvider provider,
        int port)
    {
        const string host = "sentinel-host.example.test";
        const string database = "sentinel_event_database";
        const string username = "sentinel_event_user";
        const string password = "sentinel-password-Task11";
        var services = new ServiceCollection();
        var settings = PrimaryDatabaseSettings(provider, host, port, database, username, password);
        settings["PrivacyErasure:Authority:Topology"] = "CoLocated";
        settings["PrivacyErasureAuthorityEmbedded:Path"] =
            Path.Combine(Path.GetTempPath(), $"authority-{Guid.CreateVersion7():N}.db");
        string connectionString = PrimaryDatabaseConfiguration.BuildConnectionString(
                CreatePrimaryOptions(provider, host, port, database, username, password))
            .ConnectionString;
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        OptionsValidationException? exception = await Assert.That(() =>
                services.ConfigurePersistenceServices(
                    configuration,
                    skipDbContextRegistration: true,
                    skipLookupCacheInitializer: true))
            .Throws<OptionsValidationException>();

        await Assert.That(exception!.Message)
            .Contains("PostgreSql or Sqlite", StringComparison.OrdinalIgnoreCase);
        await Assert.That(exception.Message).Contains("CoLocated", StringComparison.Ordinal);
        await Assert.That(exception.Message).Contains("EmbeddedSqlite", StringComparison.Ordinal);
        await Assert.That(exception.Message).Contains("ExternalDatabase", StringComparison.Ordinal);
        await AssertSecretSafe(exception, [password, connectionString, host, database, username]);
        await Assert.That(services.Any(descriptor =>
            descriptor.ServiceType == typeof(IPrivacyErasureAuthority))).IsFalse();
        await Assert.That(services.Any(descriptor =>
            descriptor.ServiceType == typeof(CoLocatedPrivacyErasureAuthorityDbContext))).IsFalse();
        await Assert.That(services.Any(descriptor =>
            descriptor.ServiceType == typeof(IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>))).IsFalse();
        await Assert.That(services.Any(descriptor =>
            descriptor.ServiceType == typeof(PrivacyErasureAuthorityDbContext))).IsFalse();
    }

    [Test]
    public async Task PersistenceComposition_RegistersExactlyOneTopologyAdapter()
    {
        ServiceCollection embedded = Compose(
            "EmbeddedSqlite",
            "event");
        ServiceCollection external = Compose(
            "ExternalDatabase",
            "authority");
        ServiceCollection coLocated = Compose(
            "CoLocated",
            "event");
        ServiceCollection coLocatedSqlite = Compose(
            "CoLocated",
            "event",
            PrimaryDatabaseProvider.Sqlite);

        await Assert.That(embedded.Count(descriptor =>
            descriptor.ServiceType == typeof(IPrivacyErasureAuthority))).IsEqualTo(1);
        await Assert.That(external.Count(descriptor =>
            descriptor.ServiceType == typeof(IPrivacyErasureAuthority))).IsEqualTo(1);
        await Assert.That(coLocated.Count(descriptor =>
            descriptor.ServiceType == typeof(IPrivacyErasureAuthority))).IsEqualTo(1);
        await Assert.That(coLocatedSqlite.Count(descriptor =>
            descriptor.ServiceType == typeof(IPrivacyErasureAuthority))).IsEqualTo(1);
        await Assert.That(embedded.Single(descriptor =>
            descriptor.ServiceType == typeof(IPrivacyErasureAuthority)).ImplementationType)
            .IsEqualTo(typeof(EmbeddedPrivacyErasureAuthorityRepository));
        await Assert.That(external.Single(descriptor =>
            descriptor.ServiceType == typeof(IPrivacyErasureAuthority)).ImplementationType)
            .IsEqualTo(typeof(EfCorePrivacyErasureAuthorityRepository));
        await Assert.That(coLocated.Single(descriptor =>
            descriptor.ServiceType == typeof(IPrivacyErasureAuthority)).ImplementationType)
            .IsEqualTo(typeof(CoLocatedPostgresPrivacyErasureAuthorityRepository));
        await Assert.That(coLocatedSqlite.Single(descriptor =>
            descriptor.ServiceType == typeof(IPrivacyErasureAuthority)).ImplementationType)
            .IsEqualTo(typeof(EmbeddedPrivacyErasureAuthorityRepository));
        await Assert.That(embedded.Any(descriptor =>
            descriptor.ServiceType == typeof(PrivacyErasureAuthorityDbContext))).IsFalse();
        await Assert.That(coLocated.Any(descriptor =>
            descriptor.ServiceType == typeof(CoLocatedPrivacyErasureAuthorityDbContext))).IsTrue();
        await Assert.That(coLocated.Any(descriptor =>
            descriptor.ServiceType == typeof(PrivacyErasureAuthorityDbContext))).IsFalse();
        await Assert.That(coLocatedSqlite.Any(descriptor =>
            descriptor.ServiceType == typeof(CoLocatedPrivacyErasureAuthorityDbContext))).IsFalse();
        await Assert.That(coLocatedSqlite.Any(descriptor =>
            descriptor.ServiceType == typeof(PrivacyErasureAuthorityDbContext))).IsFalse();
        await Assert.That(embedded.Count(descriptor =>
            descriptor.ServiceType == typeof(IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>)))
            .IsEqualTo(1);
        await Assert.That(external.Count(descriptor =>
            descriptor.ServiceType == typeof(PrivacyErasureAuthorityDbContext))).IsEqualTo(1);
        await Assert.That(coLocatedSqlite.Count(descriptor =>
            descriptor.ServiceType == typeof(IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>)))
            .IsEqualTo(1);
    }

    private static ServiceCollection Compose(
        string topology,
        string authorityDatabase,
        PrimaryDatabaseProvider primaryProvider = PrimaryDatabaseProvider.PostgreSql)
    {
        var services = new ServiceCollection();
        var settings = primaryProvider == PrimaryDatabaseProvider.PostgreSql
            ? PrimaryDatabaseSettings(new NpgsqlConnectionStringBuilder
            {
                Host = "localhost",
                Database = "event",
                Username = "application",
                Password = "application-canary"
            })
            : PrimaryDatabaseSettings(primaryProvider, database: Path.Combine(
                Path.GetTempPath(),
                $"event-primary-{Guid.CreateVersion7():N}.db"));
        settings["PrivacyErasure:Authority:Topology"] = topology;
        settings["PrivacyErasureAuthorityEmbedded:Path"] =
            Path.Combine(Path.GetTempPath(), $"authority-{Guid.CreateVersion7():N}.db");
        foreach (var pair in AuthorityDatabaseSettings(
                     "localhost",
                     authorityDatabase,
                     "runtime",
                     "authority-canary"))
        {
            settings[pair.Key] = pair.Value;
        }
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        services.ConfigurePersistenceServices(
            configuration,
            skipDbContextRegistration: true,
            skipLookupCacheInitializer: true);
        return services;
    }

    private static Dictionary<string, string?> PrimaryDatabaseSettings(NpgsqlConnectionStringBuilder target) => new()
    {
        ["Database:Provider"] = "PostgreSql",
        ["Database:Host"] = target.Host,
        ["Database:Port"] = target.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Database:Database"] = target.Database,
        ["Database:Runtime:Username"] = target.Username,
        ["Database:Runtime:Password"] = target.Password,
        ["Database:Migrator:Username"] = target.Username,
        ["Database:Migrator:Password"] = target.Password
    };

    private static Dictionary<string, string?> PrimaryDatabaseSettings(
        PrimaryDatabaseProvider provider,
        string host = "localhost",
        int? port = null,
        string database = "event",
        string username = "application",
        string password = "application-canary")
    {
        if (provider == PrimaryDatabaseProvider.Sqlite)
        {
            return new Dictionary<string, string?>
            {
                ["Database:Provider"] = provider.ToString(),
                ["Database:Database"] = database
            };
        }

        var settings = new Dictionary<string, string?>
        {
            ["Database:Provider"] = provider.ToString(),
            ["Database:Host"] = host,
            ["Database:Port"] = port?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Database:Database"] = database,
            ["Database:Runtime:Username"] = username,
            ["Database:Runtime:Password"] = password,
            ["Database:Runtime:TlsMode"] = "Required",
            ["Database:Migrator:Username"] = username,
            ["Database:Migrator:Password"] = password
        };

        if (provider is PrimaryDatabaseProvider.MariaDb or PrimaryDatabaseProvider.MySql)
        {
            settings["Database:Runtime:ServerFlavor"] = provider == PrimaryDatabaseProvider.MariaDb
                ? "MariaDb"
                : "MySql";
            settings["Database:Runtime:ServerVersion"] = provider == PrimaryDatabaseProvider.MariaDb
                ? "11.4"
                : "8.4";
        }

        return settings;
    }

    private static Dictionary<string, string?> AuthorityDatabaseSettings(
        string host,
        string database,
        string username,
        string password) => new()
        {
            ["PrivacyErasureAuthorityDatabase:Provider"] = "PostgreSql",
            ["PrivacyErasureAuthorityDatabase:Host"] = host,
            ["PrivacyErasureAuthorityDatabase:Port"] = "5432",
            ["PrivacyErasureAuthorityDatabase:Database"] = database,
            ["PrivacyErasureAuthorityDatabase:TlsMode"] = "Prefer",
            ["PrivacyErasureAuthorityDatabase:TrustServerCertificate"] = "false",
            ["PrivacyErasureAuthorityDatabase:Runtime:Username"] = username,
            ["PrivacyErasureAuthorityDatabase:Runtime:Password"] = password,
            ["PrivacyErasureAuthorityDatabase:Migrator:Username"] = "migrator",
            ["PrivacyErasureAuthorityDatabase:Migrator:Password"] = "migrator-canary",
        };

    private static PrimaryDatabaseConnectionOptions CreatePrimaryOptions(
        PrimaryDatabaseProvider provider,
        string host,
        int port,
        string database,
        string username,
        string password) => new()
        {
            Role = PrimaryDatabaseRole.Runtime,
            Provider = provider,
            Host = host,
            Port = port,
            Database = database,
            Username = username,
            Password = password,
            TlsMode = PrimaryDatabaseTlsMode.Required,
            ServerFlavor = provider switch
            {
                PrimaryDatabaseProvider.MariaDb => PrimaryDatabaseServerFlavor.MariaDb,
                PrimaryDatabaseProvider.MySql => PrimaryDatabaseServerFlavor.MySql,
                _ => null,
            },
            ServerVersion = provider switch
            {
                PrimaryDatabaseProvider.MariaDb => new Version(11, 4),
                PrimaryDatabaseProvider.MySql => new Version(8, 4),
                _ => null,
            },
        };

    private static async Task AssertSecretSafe(Exception exception, string[] secretMarkers)
    {
        foreach (string marker in secretMarkers.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            await Assert.That(exception.Message).DoesNotContain(marker, StringComparison.OrdinalIgnoreCase);
        }

        if (exception.InnerException is not null)
        {
            await Assert.That(exception.Message)
                .DoesNotContain(exception.InnerException.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static PrimaryDatabaseConnectionOptions CreatePostgresOptions(
        string connectionString,
        PrimaryDatabaseRole role,
        string schema)
    {
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        return new PrimaryDatabaseConnectionOptions
        {
            Role = role,
            Provider = PrimaryDatabaseProvider.PostgreSql,
            Host = target.Host,
            Port = target.Port,
            Database = target.Database,
            Schema = schema,
            Username = target.Username,
            Password = target.Password,
            TlsMode = PrimaryDatabaseTlsMode.Disabled,
        };
    }
}
