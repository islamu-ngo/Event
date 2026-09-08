using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Identity;

public static class ExternalIdentityMigrationServiceExtensions
{
    public static IServiceCollection AddExternalIdentityMigrationContext(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (IdentityDatabaseConfiguration.GetTopology(configuration)
            != IdentityDatabaseTopology.External)
        {
            return services;
        }

        services.AddDbContext<ExternalIdentityDbContext>(options =>
            IdentityDatabaseProviderComposition.Configure(
                options,
                configuration,
                PrimaryDatabaseRole.Migrator));
        return services;
    }
}
