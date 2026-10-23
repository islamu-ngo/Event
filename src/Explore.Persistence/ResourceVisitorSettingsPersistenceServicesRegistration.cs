using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Persistence.Database;
using Explore.Persistence.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Persistence;

public static class ResourceVisitorSettingsPersistenceServicesRegistration
{
    public static IServiceCollection AddResourceVisitorSettingsPersistence(this IServiceCollection services)
    {
        services.TryAddScoped<ExploreDbContextExecutionLease>();
        services.Replace(ServiceDescriptor.Scoped<IEventResourceSettingsWriter, SelectedEventResourceSettingsWriter>());
        services.Replace(ServiceDescriptor.Scoped<IVisitorAccessSettingsWriter>(provider =>
        {
            var context = provider.GetRequiredService<ExploreDbContext>();
            var lease = provider.GetRequiredService<ExploreDbContextExecutionLease>();
            var mutationLock = provider.GetRequiredService<ISettingMutationLock>();
            var unit = provider.GetRequiredService<IUnitOfWork>();
            var participation = provider.GetRequiredService<IEventParticipationConfigurationRepository>();
            var configuration = provider.GetRequiredService<IConfiguration>();
            if (lease.HumanScope is null)
                return new VisitorAccessSettingsWriter(context, mutationLock, unit, participation, configuration);
            lease.RequireOriginalOwner(context);
            return new VisitorAccessSettingsWriter(context, mutationLock, unit, participation, configuration,
                provider.GetRequiredService<IInstanceAdministratorCommitAuthority>(),
                provider.GetRequiredService<ITenantAdministratorCommitAuthority>(),
                provider.GetRequiredService<IHumanActionCommitFinalizer>(), lease.HumanScope.Subject,
                provider.GetRequiredService<IHumanSettingsMutationPreparation>(),
                provider.GetRequiredService<Explore.Application.Features.ConfigurationManifest.Importing.IHumanConfigurationImportAdmission>());
        }));
        return services;
    }
}
