using Explore.Blazor.Client.Contracts.Services.InstanceAdmin;
using Explore.Blazor.Client.Services.InstanceAdmin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Blazor.Client.Extensions;

public static class InstanceAdminServiceCollectionExtensions
{
    public static IServiceCollection AddInstanceAdminClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<InstanceAdminApiAdapter>();
        services.TryAddScoped<IInstanceOverviewService>(provider => provider.GetRequiredService<InstanceAdminApiAdapter>());
        services.TryAddScoped<IInstanceTenantService>(provider => provider.GetRequiredService<InstanceAdminApiAdapter>());
        services.TryAddScoped<IInstanceDomainService>(provider => provider.GetRequiredService<InstanceAdminApiAdapter>());
        services.TryAddScoped<IInstanceOperationsService>(provider => provider.GetRequiredService<InstanceAdminApiAdapter>());
        services.TryAddScoped<IInstancePlanCatalogService>(provider => provider.GetRequiredService<InstanceAdminApiAdapter>());
        services.TryAddScoped<IInstanceTenantConfigurationService>(provider => provider.GetRequiredService<InstanceAdminApiAdapter>());
        services.TryAddScoped<IConfigurationManifestExportService, ConfigurationManifestExportService>();
        services.TryAddScoped<LocalIdentityAdministrationService>();

        return services;
    }

}
