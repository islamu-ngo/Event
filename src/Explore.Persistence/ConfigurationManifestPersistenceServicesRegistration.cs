// ABOUTME: Registers the exact persistence graph required by configuration-manifest bootstrap.
// ABOUTME: Lets the one-shot migration host reuse canonical repositories without loading unrelated runtime services.

namespace Explore.Persistence;

using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public static class ConfigurationManifestPersistenceServicesRegistration
{
    public static IServiceCollection AddConfigurationManifestPersistence(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IUnitOfWork, EfCoreUnitOfWork>();
        services.TryAddScoped<ISettingMutationLock, RelationalSettingMutationLock>();
        services.TryAddScoped<IEmailDeliveryDisableImpactReader, EmailDeliveryDisableImpactReader>();
        services.TryAddScoped<IEmailDeliverySettingsWriter, EmailDeliverySettingsWriter>();
        services.TryAddScoped<ICoordinatedSettingMutationStore, CoordinatedSettingMutationRepository>();
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
