using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Explore.Persistence.Privacy.ErasureAuthority;

public sealed class CoLocatedPrivacyErasureAuthorityDbContextFactory
    : IDesignTimeDbContextFactory<CoLocatedPrivacyErasureAuthorityDbContext>
{
    public CoLocatedPrivacyErasureAuthorityDbContext CreateDbContext(string[] args)
    {
        IConfiguration bootstrap = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();
        if (DesignTimeModelConfiguration.IsRequested(args, bootstrap))
        {
            var modelBuilder = new DbContextOptionsBuilder<CoLocatedPrivacyErasureAuthorityDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureCoLocatedPrivacyErasureAuthorityModel(
                modelBuilder, DesignTimeModelConfiguration.ReadPrimary(bootstrap));
            return new CoLocatedPrivacyErasureAuthorityDbContext(modelBuilder.Options);
        }
        return MigrationDatabaseAcquisition.CreateDesignTimeContext(
            bootstrap, ["/database"], CreateDbContext);
    }

    private static CoLocatedPrivacyErasureAuthorityDbContext CreateDbContext(IConfiguration configuration)
    {
        PrimaryDatabaseConnectionOptions database =
            PrimaryDatabaseConfiguration.BindMigrator(configuration);
        var options = new DbContextOptionsBuilder<CoLocatedPrivacyErasureAuthorityDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureCoLocatedPrivacyErasureAuthority(
            options,
            database);
        return new CoLocatedPrivacyErasureAuthorityDbContext(options.Options);
    }
}
