namespace Explore.Application.Features.ConfigurationManifest.Application;

using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.PaidEventPolicies;
using Explore.Application.Features.ConfigurationManifest.Preflight;
using Explore.Application.Features.ConfigurationManifest.LegalDocuments;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Managed;
using Explore.Application.Features.ConfigurationManifest.Moderation;
using Explore.Application.Services;
using Explore.Application.Settings;
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
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IConfigurationManifestApplier, ConfigurationManifestApplier>();
        services.TryAddSingleton<LegalDocumentRenderingService>();
        services.TryAddScoped<IPublicationPolicyMutationBoundary, PublicationPolicyMutationBoundary>();
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
        services.TryAddScoped<ModerationConfigurationImportAdapter>();
        services.TryAddScoped<ModerationConfigurationExportService>();
        services.TryAddScoped<ConfigurationManifestCurrentStateReader>();
        services.TryAddSingleton<ConfigurationImportArtifactParser>();
        services.TryAddSingleton<ConfigurationImportPreviewComposer>();
        services.TryAddScoped<ConfigurationImportSessionManager>();
        services.TryAddScoped<ConfigurationImportSessionApplicationService>();
        services.TryAddScoped<IConfigurationImportTenantIdentityMutationBoundary,
            ConfigurationImportTenantIdentityMutationBoundary>();
        services.TryAddScoped<ConfigurationImportSectionApplier>();
        services.TryAddScoped<ConfigurationImportApplyService>();
        services.TryAddScoped<Func<IInstanceAdministratorCommitAuthority>>(provider =>
            () => provider.GetRequiredService<IInstanceAdministratorCommitAuthority>());
        services.TryAddScoped<Func<ITenantAdministratorCommitAuthority>>(provider =>
            () => provider.GetRequiredService<ITenantAdministratorCommitAuthority>());
        services.TryAddScoped<Func<IHumanActionCommitFinalizer>>(provider =>
            () => provider.GetRequiredService<IHumanActionCommitFinalizer>());
        services.TryAddScoped<ConfigurationManagedApplyScheduleService>();
        services.TryAddScoped<ConfigurationDirectTransferService>();
        services.TryAddScoped<IConfigurationImportEffectDelivery,
            ConfigurationImportEffectDelivery>();
        services.TryAddScoped<
            IConfigurationManifestEffectDispatcher,
            ConfigurationManifestEffectDispatcher>();
        return services;
    }
}
