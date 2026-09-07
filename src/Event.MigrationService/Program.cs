using Event.MigrationService.Extensions;
using Explore.Application.Configuration;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Infrastructure.ConfigurationManifest;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL;

namespace Event.MigrationService;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        var databaseOptions = builder.Configuration.AddPrimaryDatabaseBootstrap(
            builder.Environment.EnvironmentName);
        var runtimeDatabaseOptions = databaseOptions with { Role = PrimaryDatabaseRole.Runtime };

        builder.AddServiceDefaults();
        builder.Services.AddSingleton(databaseOptions);
        builder.Services.AddHostedService<Worker>();

        //builder.Services.AddOpenTelemetry()
        //  .WithTracing(tracing => tracing.AddSource(Worker.ActivitySourceName));

        builder.Services.AddPooledDbContextFactory<ExploreDbContext>(options =>
            PrimaryDatabaseProviderComposition.ConfigureApplication(options, runtimeDatabaseOptions));
        builder.Services.AddScoped(provider =>
            provider.GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContext());
        builder.Services.AddExternalIdentityMigrationContext(builder.Configuration);
        builder.Services.AddConfigurationManifestPersistence();
        builder.Services.AddConfigurationManifestStartup(
            builder.Configuration,
            ConfigurationManifestEffectDeliveryMode.DeferredToRuntime);

        builder.Services.AddDbContext<DataProtectionKeyContext>(options =>
            PrimaryDatabaseProviderComposition.ConfigureDataProtection(options, runtimeDatabaseOptions));

        PrivacyErasureAuthorityTopology erasureTopology =
            PrivacyErasureDurabilityOptions.GetTopology(builder.Configuration);
        builder.Services.AddOptions<PrivacyErasureDurabilityOptions>()
            .Configure(options => options.Topology = erasureTopology);
        if (erasureTopology == PrivacyErasureAuthorityTopology.ExternalDatabase)
        {
            PrimaryDatabaseConnectionOptions authorityDatabaseOptions =
                PrivacyErasureAuthorityDatabaseConfiguration.BindMigrator(builder.Configuration);
            PrivacyErasureAuthorityDatabaseConfiguration.EnsureDistinctPhysicalDatabase(
                databaseOptions,
                authorityDatabaseOptions);
            PrimaryDatabaseConnectionResult authorityDatabase =
                PrimaryDatabaseConfiguration.BuildConnectionString(authorityDatabaseOptions);
            builder.Services.AddDbContext<PrivacyErasureAuthorityDbContext>(options =>
                options.UseNpgsql(authorityDatabase.ConnectionString, npgsql => npgsql
                        .MigrationsAssembly(typeof(PrivacyErasureAuthorityDbContext).Assembly.FullName)
                        .MigrationsHistoryTable(
                            PrivacyErasureAuthorityDatabaseConfiguration.MigrationsHistoryTable))
                    .UseSnakeCaseNamingConvention());
        }
        else if (erasureTopology == PrivacyErasureAuthorityTopology.CoLocated)
        {
            if (databaseOptions.Provider == PrimaryDatabaseProvider.PostgreSql)
            {
                builder.Services.AddDbContext<CoLocatedPrivacyErasureAuthorityDbContext>(options =>
                    PrimaryDatabaseProviderComposition.ConfigureCoLocatedPrivacyErasureAuthority(
                        options,
                        databaseOptions));
            }
            else if (databaseOptions.Provider == PrimaryDatabaseProvider.Sqlite)
            {
                builder.Services.AddDbContext<EmbeddedPrivacyErasureAuthorityDbContext>(options =>
                    EmbeddedPrivacyErasureAuthorityDbContextFactory.ConfigureCoLocated(
                        options,
                        databaseOptions));
            }
            else
            {
                throw new InvalidOperationException(
                    PrimaryDatabaseProviderComposition.UnsupportedCoLocatedPrivacyErasureAuthorityMessage);
            }
        }
        else if (erasureTopology == PrivacyErasureAuthorityTopology.EmbeddedSqlite)
        {
            EmbeddedPrivacyErasureAuthorityOptions embedded =
                EmbeddedPrivacyErasureAuthorityOptions.Bind(builder.Configuration);
            builder.Services.AddSingleton(embedded);
            builder.Services.AddSingleton<EmbeddedPrivacyErasureAuthorityStorage>();
            builder.Services.AddDbContext<EmbeddedPrivacyErasureAuthorityDbContext>(options =>
                EmbeddedPrivacyErasureAuthorityDbContextFactory.Configure(options, embedded));
        }

        var host = builder.Build();
        host.Run();
    }
}
