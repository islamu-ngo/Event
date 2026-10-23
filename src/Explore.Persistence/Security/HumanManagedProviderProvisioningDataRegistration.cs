using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Security;

public static class HumanManagedProviderProvisioningDataRegistration
{
    /// <summary>
    /// Opt-in source composition only. Parent owns original Global selection, source
    /// preparation, actual physical settlement and migration installation.
    /// </summary>
    public static IServiceCollection AddHumanManagedProviderProvisioningData(this IServiceCollection services)
    {
        services.AddScoped<IHumanManagedProviderProvisioningDataSource>(provider =>
        {
            var context = provider.GetRequiredService<ExploreDbContext>();
            HumanSourceProviderOperations.RequirePostgresOrSqlite(context, "human_provisioning_data_provider_not_supported");
            var configuration = provider.GetRequiredService<IConfiguration>();
            if (IdentityDatabaseConfiguration.GetTopology(configuration) != IdentityDatabaseTopology.Colocated)
                throw new NotSupportedException("human_provisioning_data_requires_colocated_identity_sources");
            return new HumanManagedProviderProvisioningDataSource(context,
                provider.GetRequiredService<ExploreDbContextExecutionLease>(),
                provider.GetRequiredService<TimeProvider>(),
                HumanSourceProviderOperations.BindEnrollment(context, () => configuration));
        });
        return services;
    }
}
