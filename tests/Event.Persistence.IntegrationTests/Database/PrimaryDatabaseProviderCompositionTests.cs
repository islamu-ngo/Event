// ABOUTME: Verifies every supported primary provider uses the shared EF Core composition switch.
// ABOUTME: Covers provider identity, migration ownership, server flavor, Data Protection, and design-time projection.

using Explore.Application.Contracts.Persistence;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Extensions;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Database;

public sealed class PrimaryDatabaseProviderCompositionTests
{
    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql, "NpgsqlOptionsExtension")]
    [Arguments(PrimaryDatabaseProvider.Sqlite, "SqliteOptionsExtension")]
    [Arguments(PrimaryDatabaseProvider.SqlServer, "SqlServerOptionsExtension")]
    [Arguments(PrimaryDatabaseProvider.MariaDb, "MySqlOptionsExtension")]
    [Arguments(PrimaryDatabaseProvider.MySql, "MySqlOptionsExtension")]
    public async Task ConfigureApplication_SelectsRequestedProvider(
        PrimaryDatabaseProvider provider,
        string expectedOptionsExtension)
    {
        var builder = CreateTestOptionsBuilder<ExploreDbContext>();

        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, CreateOptions(provider));

        await Assert.That(builder.Options.Extensions.Select(extension => extension.GetType().Name))
            .Contains(expectedOptionsExtension);
    }

    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql, "NpgsqlOptionsExtension")]
    [Arguments(PrimaryDatabaseProvider.Sqlite, "SqliteOptionsExtension")]
    [Arguments(PrimaryDatabaseProvider.SqlServer, "SqlServerOptionsExtension")]
    [Arguments(PrimaryDatabaseProvider.MariaDb, "MySqlOptionsExtension")]
    [Arguments(PrimaryDatabaseProvider.MySql, "MySqlOptionsExtension")]
    public async Task ConfigurePersistenceServices_SelectsRequestedProvider(
        PrimaryDatabaseProvider provider,
        string expectedOptionsExtension)
    {
        var services = new ServiceCollection();
        services.ConfigurePersistenceServices(
            BuildConfiguration(provider),
            skipLookupCacheInitializer: true,
            environmentName: "Production");
        services.AddDbContext<ExploreDbContext>(ConfigureTestOptions);
        using var serviceProvider = services.BuildIsolatedServiceProvider();

        var options = serviceProvider.GetRequiredService<DbContextOptions<ExploreDbContext>>();

        await Assert.That(options.Extensions.Select(extension => extension.GetType().Name))
            .Contains(expectedOptionsExtension);
    }

    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql, "NpgsqlOptionsExtension")]
    [Arguments(PrimaryDatabaseProvider.Sqlite, "SqliteOptionsExtension")]
    [Arguments(PrimaryDatabaseProvider.SqlServer, "SqlServerOptionsExtension")]
    [Arguments(PrimaryDatabaseProvider.MariaDb, "MySqlOptionsExtension")]
    [Arguments(PrimaryDatabaseProvider.MySql, "MySqlOptionsExtension")]
    public async Task PrimaryProviderMatrix_SupportsApplicationAndDataProtection(
        PrimaryDatabaseProvider provider,
        string expectedOptionsExtension)
    {
        var application = CreateTestOptionsBuilder<ExploreDbContext>();
        var dataProtection = CreateTestOptionsBuilder<DataProtectionKeyContext>();
        PrimaryDatabaseConnectionOptions options = CreateOptions(provider);

        PrimaryDatabaseProviderComposition.ConfigureApplication(application, options);
        PrimaryDatabaseProviderComposition.ConfigureDataProtection(dataProtection, options);

        await Assert.That(application.Options.Extensions.Select(extension => extension.GetType().Name))
            .Contains(expectedOptionsExtension);
        await Assert.That(dataProtection.Options.Extensions.Select(extension => extension.GetType().Name))
            .Contains(expectedOptionsExtension);
    }

    [Test]
    public async Task ConfigurePersistenceServices_DoesNotEnablePostgresRlsForOtherProviders()
    {
        var values = BuildConfigurationValues(PrimaryDatabaseProvider.Sqlite);
        values["Persistence:EnableRlsTenantSession"] = "true";
        var services = new ServiceCollection();
        services.ConfigurePersistenceServices(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            skipLookupCacheInitializer: true,
            environmentName: "Production");
        services.AddDbContext<ExploreDbContext>(ConfigureTestOptions);
        using var serviceProvider = services.BuildIsolatedServiceProvider();

        var options = serviceProvider.GetRequiredService<DbContextOptions<ExploreDbContext>>();
        var interceptors = options.FindExtension<CoreOptionsExtension>()?.Interceptors ?? [];

        await Assert.That(interceptors.Any(interceptor => interceptor is PostgresTenantSessionInterceptor)).IsFalse();
    }

    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql, false, false)]
    [Arguments(PrimaryDatabaseProvider.Sqlite, false, true)]
    [Arguments(PrimaryDatabaseProvider.SqlServer, false, false)]
    [Arguments(PrimaryDatabaseProvider.MariaDb, true, false)]
    [Arguments(PrimaryDatabaseProvider.MySql, true, false)]
    public async Task ConfigurePersistenceServices_SelectsProviderNeutralLocksAndTransactionInterceptors(
        PrimaryDatabaseProvider provider,
        bool expectsMySqlInterceptor,
        bool expectsSqliteInterceptors)
    {
        var services = new ServiceCollection();
        services.ConfigurePersistenceServices(
            BuildConfiguration(provider),
            skipLookupCacheInitializer: true,
            environmentName: "Production");
        services.AddDbContext<ExploreDbContext>(ConfigureTestOptions);
        using var serviceProvider = services.BuildIsolatedServiceProvider();

        await Assert.That(services.Single(service => service.ServiceType == typeof(ISettingMutationLock))
            .ImplementationType).IsEqualTo(typeof(RelationalSettingMutationLock));
        await Assert.That(services.Single(service => service.ServiceType == typeof(IAtprotoSessionRefreshLock))
            .ImplementationType).IsEqualTo(typeof(RelationalAtprotoSessionRefreshLock));
        ServiceDescriptor coordinatedStoreDescriptor = services.Single(service =>
            service.ServiceType == typeof(ICoordinatedSettingMutationStore));
        await Assert.That(coordinatedStoreDescriptor.ImplementationType)
            .IsEqualTo(typeof(CoordinatedSettingMutationRepository));
        await Assert.That(coordinatedStoreDescriptor.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        using (IServiceScope scope = serviceProvider.CreateScope())
        {
            ICoordinatedSettingMutationStore resolved =
                scope.ServiceProvider.GetRequiredService<ICoordinatedSettingMutationStore>();
            await Assert.That(resolved.GetType()).IsEqualTo(typeof(CoordinatedSettingMutationRepository));
        }
        var options = serviceProvider.GetRequiredService<DbContextOptions<ExploreDbContext>>();
        var interceptors = options.FindExtension<CoreOptionsExtension>()?.Interceptors ?? [];

        await Assert.That(interceptors.Count(interceptor =>
                interceptor is MySqlNamedLockTransactionInterceptor))
            .IsEqualTo(expectsMySqlInterceptor ? 1 : 0);
        await Assert.That(interceptors.Count(interceptor =>
                interceptor is SqliteNamedLockTransactionInterceptor))
            .IsEqualTo(expectsSqliteInterceptors ? 1 : 0);
        await Assert.That(interceptors.Count(interceptor =>
                interceptor is SqliteProjectionLockTransactionInterceptor))
            .IsEqualTo(expectsSqliteInterceptors ? 1 : 0);
    }

    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql, false, false)]
    [Arguments(PrimaryDatabaseProvider.Sqlite, false, true)]
    [Arguments(PrimaryDatabaseProvider.SqlServer, false, false)]
    [Arguments(PrimaryDatabaseProvider.MariaDb, true, false)]
    [Arguments(PrimaryDatabaseProvider.MySql, true, false)]
    public async Task ConfigureApplication_InstallsSharedTransactionLockInterceptors(
        PrimaryDatabaseProvider provider,
        bool expectsMySqlInterceptor,
        bool expectsSqliteInterceptors)
    {
        var builder = CreateTestOptionsBuilder<ExploreDbContext>();

        PrimaryDatabaseProviderComposition.ConfigureApplication(
            builder,
            CreateOptions(provider));

        var interceptors = builder.Options
            .FindExtension<CoreOptionsExtension>()?.Interceptors ?? [];
        await Assert.That(interceptors.Count(interceptor =>
                interceptor is MySqlNamedLockTransactionInterceptor))
            .IsEqualTo(expectsMySqlInterceptor ? 1 : 0);
        await Assert.That(interceptors.Count(interceptor =>
                interceptor is SqliteNamedLockTransactionInterceptor))
            .IsEqualTo(expectsSqliteInterceptors ? 1 : 0);
        await Assert.That(interceptors.Count(interceptor =>
                interceptor is SqliteProjectionLockTransactionInterceptor))
            .IsEqualTo(expectsSqliteInterceptors ? 1 : 0);
    }

    [Test]
    [Arguments(PrimaryDatabaseProvider.MariaDb, "MariaDbServerVersion")]
    [Arguments(PrimaryDatabaseProvider.MySql, "MySqlServerVersion")]
    public async Task ConfigureApplication_SelectsMicrotingServerFlavor(
        PrimaryDatabaseProvider provider,
        string expectedServerVersionType)
    {
        var builder = CreateTestOptionsBuilder<ExploreDbContext>();

        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, CreateOptions(provider));

        var extension = builder.Options.Extensions.Single(candidate =>
            candidate.GetType().Name == "MySqlOptionsExtension");
        var serverVersion = extension.GetType().GetProperty("ServerVersion")!.GetValue(extension);
        await Assert.That(serverVersion!.GetType().Name).IsEqualTo(expectedServerVersionType);
    }

    [Test]
    public async Task MigrationAssemblyContract_IsStableForAllPrimaryContexts()
    {
        await Assert.That(PrimaryDatabaseProviderComposition.GetMigrationsAssemblyName(
                PrimaryDatabaseProvider.PostgreSql,
                PrimaryDatabaseMigrationTarget.Application)
            ).IsEqualTo("Explore.Persistence");
        await Assert.That(PrimaryDatabaseProviderComposition.GetMigrationsAssemblyName(
                PrimaryDatabaseProvider.PostgreSql,
                PrimaryDatabaseMigrationTarget.DataProtection)
            ).IsEqualTo("Explore.Persistence");
        await Assert.That(PrimaryDatabaseProviderComposition.GetMigrationsAssemblyName(
                PrimaryDatabaseProvider.PostgreSql,
                PrimaryDatabaseMigrationTarget.CoLocatedPrivacyErasureAuthority)
            ).IsEqualTo("Explore.Persistence");

        foreach (var provider in Enum.GetValues<PrimaryDatabaseProvider>()
                     .Where(candidate => candidate != PrimaryDatabaseProvider.PostgreSql))
        {
            string expectedApplicationAssembly = provider == PrimaryDatabaseProvider.MariaDb
                ? "Explore.Persistence.Migrations.MySql"
                : $"Explore.Persistence.Migrations.{provider}";
            string expectedDataProtectionAssembly = provider == PrimaryDatabaseProvider.MariaDb
                ? "Explore.Persistence.DataProtection.Migrations.MySql"
                : $"Explore.Persistence.DataProtection.Migrations.{provider}";

            await Assert.That(PrimaryDatabaseProviderComposition.GetMigrationsAssemblyName(
                    provider,
                    PrimaryDatabaseMigrationTarget.Application)
                ).IsEqualTo(expectedApplicationAssembly);
            await Assert.That(PrimaryDatabaseProviderComposition.GetMigrationsAssemblyName(
                    provider,
                    PrimaryDatabaseMigrationTarget.DataProtection)
                ).IsEqualTo(expectedDataProtectionAssembly);
            await Assert.That(() => PrimaryDatabaseProviderComposition.GetMigrationsAssemblyName(
                provider,
                PrimaryDatabaseMigrationTarget.CoLocatedPrivacyErasureAuthority)).Throws<InvalidOperationException>();
        }
    }

    [Test]
    public async Task MigrationHistoryContract_SeparatesApplicationAndDataProtection()
    {
        foreach (var provider in Enum.GetValues<PrimaryDatabaseProvider>())
        {
            var application = PrimaryDatabaseProviderComposition.GetMigrationsHistoryTable(
                provider,
                PrimaryDatabaseMigrationTarget.Application);
            var dataProtection = PrimaryDatabaseProviderComposition.GetMigrationsHistoryTable(
                provider,
                PrimaryDatabaseMigrationTarget.DataProtection);

            await Assert.That(application.Table).IsNotEqualTo(dataProtection.Table);
            await Assert.That(application.Schema).IsEqualTo(dataProtection.Schema);

            if (provider is PrimaryDatabaseProvider.PostgreSql or PrimaryDatabaseProvider.SqlServer)
            {
                await Assert.That(application).IsEqualTo(("__EFMigrationsHistory", "islamu_event"));
                await Assert.That(dataProtection).IsEqualTo(("__EFDataProtectionMigrationsHistory", "islamu_event"));
            }
            else
            {
                await Assert.That(application).IsEqualTo(("ie___EFMigrationsHistory", (string?)null));
                await Assert.That(dataProtection).IsEqualTo(("ie___EFDataProtectionMigrationsHistory", (string?)null));
            }
        }

        await Assert.That(PrimaryDatabaseProviderComposition.GetMigrationsHistoryTable(
                PrimaryDatabaseProvider.PostgreSql,
                PrimaryDatabaseMigrationTarget.CoLocatedPrivacyErasureAuthority,
                "custom_event")
            ).IsEqualTo(("__EFPrivacyErasureAuthorityMigrationsHistory", "custom_event"));
    }

    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql, "islamu_event", null)]
    [Arguments(PrimaryDatabaseProvider.SqlServer, "islamu_event", null)]
    [Arguments(PrimaryDatabaseProvider.Sqlite, null, "ie_")]
    [Arguments(PrimaryDatabaseProvider.MariaDb, null, "ie_")]
    [Arguments(PrimaryDatabaseProvider.MySql, null, "ie_")]
    public async Task DataProtectionModel_UsesFixedNamespacePolicy(
        PrimaryDatabaseProvider provider,
        string? expectedSchema,
        string? expectedPrefix)
    {
        var builder = CreateTestOptionsBuilder<DataProtectionKeyContext>();
        PrimaryDatabaseProviderComposition.ConfigureDataProtection(builder, CreateOptions(provider));
        using var context = new DataProtectionKeyContext(builder.Options);

        var entityType = context.Model.FindEntityType(typeof(DataProtectionKey))!;

        await Assert.That(entityType.GetSchema()).IsEqualTo(expectedSchema);
        if (expectedPrefix is not null)
        {
            await Assert.That(entityType.GetTableName()).StartsWith(expectedPrefix);
        }
    }

    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql, "Npgsql.EntityFrameworkCore.PostgreSQL")]
    [Arguments(PrimaryDatabaseProvider.Sqlite, "Microsoft.EntityFrameworkCore.Sqlite")]
    [Arguments(PrimaryDatabaseProvider.SqlServer, "Microsoft.EntityFrameworkCore.SqlServer")]
    [Arguments(PrimaryDatabaseProvider.MariaDb, "Microting.EntityFrameworkCore.MySql")]
    [Arguments(PrimaryDatabaseProvider.MySql, "Microting.EntityFrameworkCore.MySql")]
    public async Task AddExploreDataProtection_SelectsRequestedProvider(
        PrimaryDatabaseProvider provider,
        string expectedProviderName)
    {
        var services = new ServiceCollection();
        services.AddExploreDataProtection(BuildConfiguration(provider));
        services.AddDbContext<DataProtectionKeyContext>(ConfigureTestOptions);
        using var serviceProvider = services.BuildIsolatedServiceProvider();

        using var context = serviceProvider.GetRequiredService<DataProtectionKeyContext>();

        await Assert.That(context.Database.ProviderName).IsEqualTo(expectedProviderName);
    }

    [Test]
    [TUnit.Core.Executors.TestExecutor<FreshEfProcessExecutor>]
    public async Task DesignTimeFactories_UseStructuredPostgresMigratorSettings()
    {
        var values = new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSql",
            ["Database:Host"] = "pg.example.test",
            ["Database:Database"] = "event_db",
            ["Database:Migrator:Username"] = "migrator",
            ["Database:Migrator:Password"] = Guid.CreateVersion7().ToString("N"),
        };

        using var application = new ExploreDbContextFactory().CreateDbContext(
            new ConfigurationBuilder().AddInMemoryCollection(values));
        using var dataProtection = new DataProtectionKeyContextFactory().CreateDbContext(
            new ConfigurationBuilder().AddInMemoryCollection(values));

        await Assert.That(application.Database.ProviderName).IsEqualTo("Npgsql.EntityFrameworkCore.PostgreSQL");
        await Assert.That(dataProtection.Database.ProviderName).IsEqualTo("Npgsql.EntityFrameworkCore.PostgreSQL");
    }

    [Test]
    [TUnit.Core.Executors.TestExecutor<FreshEfProcessExecutor>]
    public async Task DesignTimeFactories_PreserveExplicitStructuredProviderPriority()
    {
        var values = new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:Database"] = "factory-event.db",
            ["Postgresql:Host"] = "ignored.example.test",
            ["Postgresql:Database"] = "ignored",
            ["Postgresql:Username"] = "ignored",
            ["Postgresql:Password"] = "ignored-secret",
        };

        using var application = new ExploreDbContextFactory().CreateDbContext(
            new ConfigurationBuilder().AddInMemoryCollection(values));
        using var dataProtection = new DataProtectionKeyContextFactory().CreateDbContext(
            new ConfigurationBuilder().AddInMemoryCollection(values));

        await Assert.That(application.Database.ProviderName).IsEqualTo("Microsoft.EntityFrameworkCore.Sqlite");
        await Assert.That(dataProtection.Database.ProviderName).IsEqualTo("Microsoft.EntityFrameworkCore.Sqlite");
    }

    private static IConfiguration BuildConfiguration(PrimaryDatabaseProvider provider)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(BuildConfigurationValues(provider))
            .Build();
    }

    private static Dictionary<string, string?> BuildConfigurationValues(PrimaryDatabaseProvider provider)
    {
        var options = CreateOptions(provider);
        return new Dictionary<string, string?>
        {
            ["Database:Provider"] = provider.ToString(),
            ["Database:Host"] = options.Host,
            ["Database:Port"] = options.Port?.ToString(),
            ["Database:Database"] = options.Database,
            ["Database:Runtime:Username"] = options.Username,
            ["Database:Runtime:Password"] = options.Password,
            ["Database:Runtime:TlsMode"] = options.TlsMode.ToString(),
            ["Database:Runtime:ServerFlavor"] = options.ServerFlavor?.ToString(),
            ["Database:Runtime:ServerVersion"] = options.ServerVersion?.ToString(),
        };
    }

    private static PrimaryDatabaseConnectionOptions CreateOptions(PrimaryDatabaseProvider provider) =>
        provider == PrimaryDatabaseProvider.Sqlite
            ? new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Runtime,
                Provider = provider,
                Database = "event.db",
            }
            : new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Runtime,
                Provider = provider,
                Host = "database.example.test",
                Database = "event_db",
                Username = "event_user",
                Password = Guid.CreateVersion7().ToString("N"),
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

    private static DbContextOptionsBuilder<TContext> CreateTestOptionsBuilder<TContext>()
        where TContext : DbContext
    {
        var builder = TestDbContextOptions.Create<TContext>();
        ConfigureTestOptions(builder);
        return builder;
    }

    private static void ConfigureTestOptions(DbContextOptionsBuilder builder) => TestDbContextOptions.Apply(builder);
}
