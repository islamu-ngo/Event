using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Explore.Persistence.Identity;

public static class ExternalIdentityDatabaseMigrator
{
    public static async Task<bool> MigrateIfExternalAsync(
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        if (IdentityDatabaseConfiguration.GetTopology(configuration)
            != IdentityDatabaseTopology.External)
        {
            return false;
        }

        var options = new DbContextOptionsBuilder<ExternalIdentityDbContext>();
        IdentityDatabaseProviderComposition.Configure(
            options,
            configuration,
            PrimaryDatabaseRole.Migrator);
        await using var context = new ExternalIdentityDbContext(options.Options);

        logger.LogInformation(
            "Applying external Local Identity database migrations.");
        await context.Database.MigrateAsync(cancellationToken);
        return true;
    }
}
