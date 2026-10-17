namespace Explore.Persistence;

using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public static class ConfigurationManifestPersistenceServicesRegistration
{
    public static IServiceCollection AddConfigurationManifestPersistence(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<ConfigurationManifestBootstrapOrigin>();
        services.TryAddScoped<IConfigurationManifestBootstrapSourceAuthority>(provider =>
            provider.GetRequiredService<ConfigurationManifestBootstrapOrigin>());
        services.TryAddScoped<IConfigurationManifestBootstrapExecutor, ConfigurationManifestBootstrapExecutor>();
        services.TryAddScoped<IConfigurationManifestBootstrapOwnerFactory, ConfigurationManifestBootstrapOwnerFactory>();
        services.TryAddScoped<IConfigurationManifestStartupRunner, ConfigurationManifestStartupRunner>();
        services.TryAddScoped<IConfigurationManifestPostMigrationSequence, ConfigurationManifestPostMigrationSequence>();
        services.TryAddScoped<IUnitOfWork, EfCoreUnitOfWork>();
        services.TryAddScoped<IOriginalOwnerSettlementBoundary>(provider =>
            (IOriginalOwnerSettlementBoundary)provider.GetRequiredService<IUnitOfWork>());
        services.TryAddScoped<ISettingMutationLock, RelationalSettingMutationLock>();
        services.TryAddScoped<IEmailDeliveryDisableImpactReader, EmailDeliveryDisableImpactReader>();
        services.TryAddScoped<IHumanActionCommitFinalizer, HumanActionCommitFinalizer>();
        services.TryAddScoped<HumanSettingsDataBoundary>();
        services.TryAddScoped<IEmailDeliverySettingsWriter, EmailDeliverySettingsWriter>();
        services.AddResourceVisitorSettingsPersistence();
        services.TryAddScoped<IEventParticipationConfigurationRepository, EventParticipationConfigurationRepository>();
        services.TryAddScoped<ITenantRepository, TenantRepository>();
        services.TryAddScoped<ISystemSettingRepository, SystemSettingRepository>();
        services.TryAddScoped<ITenantSettingRepository, TenantSettingRepository>();
        services.TryAddScoped<IPaidEventPolicyRepository, PaidEventPolicyRepository>();
        services.TryAddScoped<
            ITenantSettingsDocumentRepository,
            TenantSettingsDocumentRepository>();
        services.TryAddScoped<
            IConfigurationManifestOperationRepository,
            ConfigurationManifestOperationRepository>();
        services.TryAddScoped<
            IConfigurationManifestFailureRecorder,
            ConfigurationManifestFailureRepository>();
        services.TryAddScoped<
            IConfigurationManifestEffectOutboxRepository,
            OutboxRepository>();

        return services;
    }
}
