using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Operations;
using Explore.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Infrastructure;

public static class HumanSettingsMutationPreparationServicesRegistration
{
    /// <summary>
    /// Registers only the scoped issuer. Source ports and owner resolve lazily after selection;
    /// the parent composition must call this extension in each actual human execution scope.
    /// </summary>
    public static IServiceCollection AddHumanSettingsMutationPreparation(this IServiceCollection services)
    {
        services.TryAddScoped<IHumanSettingsMutationPreparation, HumanSettingsMutationPreparation>();
        services.TryAddScoped<IHumanTenantPreferencePreparation, HumanTenantPreferencePreparation>();
        services.TryAddScoped<IHumanLearningMutationPreparation, HumanLearningMutationPreparation>();
        services.TryAddScoped<IHumanLearningContextReader>(provider => new HumanLearningContextReader(
            provider.GetRequiredService<Explore.Application.Contracts.Operations.IHumanCommitScopePreparation>(),
            provider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>(),
            provider.GetRequiredService<Explore.Application.Authentication.IHumanAuthenticationLifetimeObserver>(),
            provider.GetRequiredService<Explore.Application.Contracts.Identity.IMachinePrincipalAccessor>(),
            provider.GetRequiredService<Explore.Application.Contracts.Infrastructure.ITenantContext>(),
            provider.GetRequiredService<TimeProvider>(),
            () => provider.GetRequiredService<IHumanOriginalCredentialCustody>(),
            () => provider.GetRequiredService<IHumanPrincipalSourceFence>(),
            () => provider.GetRequiredService<IHumanSupportAccessSourceFactory>(),
            () => provider.GetRequiredService<IHumanEventAssignmentSourceFactory>(),
            () => provider.GetRequiredService<IPolicySourceSetFence>(),
            () => provider.GetRequiredService<ISerializableAttemptOwner>(),
            () => provider.GetRequiredService<IHumanActionCommitFinalizer>(),
            () => provider.GetRequiredService<ILearningRepository>(),
            () => provider.GetRequiredService<IActorRepository>(),
            () => provider.GetRequiredService<IAiActorContextRepository>(),
            () => provider.GetRequiredService<ITenantAdministratorCommitAuthority>(),
            () => provider.GetRequiredService<Explore.Application.Contracts.Infrastructure.IHierarchicalSettingsResolver>(),
            () => provider.GetRequiredService<Explore.Application.Contracts.Infrastructure.Ai.IAiProviderCapabilityResolver>(),
            () => provider.GetRequiredService<Explore.Application.Contracts.PrivacyErasure.IPrivacyIdentityFenceAuthority>()));
        return services;
    }
}
