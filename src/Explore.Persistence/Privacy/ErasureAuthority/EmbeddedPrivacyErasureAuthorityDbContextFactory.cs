using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Explore.Persistence.Privacy.ErasureAuthority;

public sealed class EmbeddedPrivacyErasureAuthorityDbContextFactory
    : IDesignTimeDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>
{
    public EmbeddedPrivacyErasureAuthorityDbContext CreateDbContext(string[] args)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();
        return CreateDbContext(configuration);
    }

    public EmbeddedPrivacyErasureAuthorityDbContext CreateDbContext(IConfiguration configuration)
    {
        EmbeddedPrivacyErasureAuthorityOptions embedded =
            EmbeddedPrivacyErasureAuthorityOptions.Bind(configuration);
        var options = new DbContextOptionsBuilder<EmbeddedPrivacyErasureAuthorityDbContext>();
        Configure(options, embedded);
        return new EmbeddedPrivacyErasureAuthorityDbContext(options.Options);
    }

    public static void Configure(
        DbContextOptionsBuilder options,
        EmbeddedPrivacyErasureAuthorityOptions embedded) =>
        ConfigureSqlite(options, embedded.BuildConnectionString(), embedded.BusyTimeoutSeconds);

    public static void ConfigureCoLocated(
        DbContextOptionsBuilder options,
        PrimaryDatabaseConnectionOptions primaryDatabase)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(primaryDatabase);
        if (primaryDatabase.Provider != PrimaryDatabaseProvider.Sqlite)
        {
            throw new InvalidOperationException(
                "CoLocated SQLite authority composition requires Database:Provider=Sqlite.");
        }

        PrimaryDatabaseConnectionResult database =
            PrimaryDatabaseConfiguration.BuildConnectionString(primaryDatabase);
        ConfigureSqlite(
            options,
            database.ConnectionString,
            EmbeddedPrivacyErasureAuthorityOptions.DefaultBusyTimeoutSeconds);
    }

    private static void ConfigureSqlite(
        DbContextOptionsBuilder options,
        string connectionString,
        int busyTimeoutSeconds) =>
        options.UseSqlite(
                connectionString,
                sqlite => sqlite
                    .MigrationsAssembly(EmbeddedPrivacyErasureAuthorityDbContext.MigrationsAssembly)
                    .MigrationsHistoryTable(EmbeddedPrivacyErasureAuthorityDbContext.MigrationsHistoryTable))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(EmbeddedPrivacyErasureAuthorityConnectionInterceptor.For(
                busyTimeoutSeconds));
}
