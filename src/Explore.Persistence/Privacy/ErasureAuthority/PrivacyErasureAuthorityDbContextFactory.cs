using Explore.Secrets.Configuration;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Explore.Persistence.Privacy.ErasureAuthority;

public sealed class PrivacyErasureAuthorityDbContextFactory
    : IDesignTimeDbContextFactory<PrivacyErasureAuthorityDbContext>
{
    public PrivacyErasureAuthorityDbContext CreateDbContext(string[] args)
    {
        IConfiguration bootstrap = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();
        return CreateDbContext(SecretAuthorityConfiguration.Build(
            bootstrap,
            SecretAuthorityConfiguration.GetEnvironmentName(bootstrap),
            "/database/erasure"));
    }

    public PrivacyErasureAuthorityDbContext CreateDbContext(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var database = PrivacyErasureAuthorityDatabaseConfiguration
            .ResolveMigratorConnectionString(configuration);

        var options = new DbContextOptionsBuilder<PrivacyErasureAuthorityDbContext>()
            .UseNpgsql(
                database.ConnectionString,
                npgsql => npgsql
                    .MigrationsAssembly("Explore.Persistence")
                    .MigrationsHistoryTable(
                        PrivacyErasureAuthorityDatabaseConfiguration.MigrationsHistoryTable))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new PrivacyErasureAuthorityDbContext(options);
    }
}
