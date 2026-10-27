using Event.MigrationService.Extensions;
using Explore.Application.Configuration;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;

namespace Event.MigrationService;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        await using var authority = await MigrationDatabaseAcquisition.OpenAsync(
            builder.Configuration, builder.Environment.EnvironmentName,
            ["/database", "/database/erasure", "/database/identity"]);
        var databaseOptions = builder.Configuration.AddPrimaryDatabaseBootstrap(authority);
        ExploreDatabaseMigrator.EnsureAgentBrowserAdmission(builder.Configuration, builder.Environment);

        builder.AddServiceDefaults();
        builder.Services.AddSingleton(databaseOptions);
        builder.Services.AddHostedService<Worker>();

        //builder.Services.AddOpenTelemetry()
        //  .WithTracing(tracing => tracing.AddSource(Worker.ActivitySourceName));

        builder.Services.AddPooledDbContextFactory<ExploreDbContext>(options =>
            PrimaryDatabaseProviderComposition.ConfigureApplication(options, databaseOptions));
        builder.Services.AddScoped(provider =>
            provider.GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContext());
        builder.Services.AddExternalIdentityMigrationContext(builder.Configuration);
        builder.Services.AddDbContext<DataProtectionKeyContext>(options =>
            PrimaryDatabaseProviderComposition.ConfigureDataProtection(options, databaseOptions));

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

        using var host = builder.Build();
        await host.RunAsync();
    }
}
