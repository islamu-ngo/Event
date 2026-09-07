namespace Explore.Application.Features.ConfigurationManifest.Application;

using Explore.Application.Contracts.Services;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.PaidEventPolicies;
using Explore.Application.Features.ConfigurationManifest.Preflight;
using Explore.Application.Features.ConfigurationManifest.Handlers.Commands;
using Explore.Application.Features.ConfigurationManifest.LegalDocuments;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Managed;
using Explore.Application.Services;
using Explore.Application.Settings;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public enum ConfigurationManifestEffectDeliveryMode
{
    Immediate,
    DeferredToRuntime
}

public static class ConfigurationManifestApplicationServicesRegistration
{
    public static IServiceCollection AddConfigurationManifestApplication(
        this IServiceCollection services,
        ConfigurationManifestEffectDeliveryMode effectDeliveryMode =
            ConfigurationManifestEffectDeliveryMode.Immediate)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!Enum.IsDefined(effectDeliveryMode))
        {
            throw new ArgumentOutOfRangeException(nameof(effectDeliveryMode));
        }

        services.TryAddScoped<IPublicationPolicyMutationBoundary, PublicationPolicyMutationBoundary>();
        services.TryAddTransient<IMediator>(provider => new Mediator(provider));
        services.TryAddScoped<SettingUpsertService>();
        services.TryAddScoped<
            IPaidEventPolicyMutationBoundary,
            PaidEventPolicyMutationBoundary>();
        services.TryAddScoped<ITenantCreationService, TenantCreationService>();
        services.TryAddScoped<
            IConfigurationManifestTenantSettingMutationBoundary,
            ConfigurationManifestTenantSettingMutationBoundary>();
        services.TryAddScoped<
            IConfigurationManifestInstanceSettingMutationBoundary,
            ConfigurationManifestInstanceSettingMutationBoundary>();
        services.TryAddScoped<
            ITenantBrandingSettingsDocumentLockService,
            TenantBrandingSettingsDocumentLockService>();
        services.TryAddScoped<
            IConfigurationManifestPreflight,
            ConfigurationManifestPreflight>();
        services.TryAddScoped<ApplyConfigurationManifestCommandHandler>();
        services.TryAddScoped<IConfigurationManifestApplier>(provider =>
            provider.GetRequiredService<ApplyConfigurationManifestCommandHandler>());
        services.TryAddSingleton<LegalDocumentRenderingService>();

        if (effectDeliveryMode == ConfigurationManifestEffectDeliveryMode.DeferredToRuntime)
        {
            services.TryAddScoped<
                IConfigurationManifestEffectDeliveryStrategy,
                DeferredConfigurationManifestEffectDelivery>();
        }
        else
        {
            services.TryAddSingleton<ConfigurationImportArtifactParser>();
            services.TryAddSingleton<ConfigurationImportPreviewComposer>();
            services.TryAddScoped<ConfigurationImportSessionManager>();
            services.TryAddScoped<ConfigurationImportSessionApplicationService>();
            services.TryAddScoped<IConfigurationImportTenantIdentityMutationBoundary,
                ConfigurationImportTenantIdentityMutationBoundary>();
            services.TryAddScoped<ConfigurationImportSectionApplier>();
            services.TryAddScoped<ConfigurationImportApplyService>();
            services.TryAddScoped<ConfigurationManagedApplyScheduleService>();
            services.TryAddScoped<ConfigurationDirectTransferService>();
            services.TryAddScoped<IConfigurationImportEffectDelivery,
                ConfigurationImportEffectDelivery>();
            services.TryAddScoped<
                IConfigurationManifestEffectDispatcher,
                ConfigurationManifestEffectDispatcher>();
            services.TryAddScoped<
                IConfigurationManifestEffectDeliveryStrategy,
                ConfigurationManifestEffectDelivery>();
        }

        return services;
    }
}
