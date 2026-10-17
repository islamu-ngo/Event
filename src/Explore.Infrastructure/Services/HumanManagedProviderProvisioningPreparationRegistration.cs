using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Features.ManagedProviderProvisioning;
using Explore.Application.Services;
using Explore.Application.Management;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services;

public static class HumanManagedProviderProvisioningPreparationRegistration
{
    public static IServiceCollection AddHumanManagedProviderProvisioningPreparation(this IServiceCollection services)
    {
        services.AddScoped<IHumanManagedProviderProvisioningPreparation>(provider =>
            new HumanManagedProviderProvisioningPreparation(
                provider.GetRequiredService<IHumanCommitScopePreparation>(),
                provider.GetRequiredService<IHttpContextAccessor>(),
                provider.GetRequiredService<IHumanAuthenticationLifetimeObserver>(),
                provider.GetRequiredService<IMachinePrincipalAccessor>(),
                provider.GetRequiredService<ITenantContext>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<IOptions<AuthorizationProviderDeploymentOptions>>(),
                provider.GetRequiredService<IOptions<CerbosSettings>>(),
                () => provider.GetRequiredService<IHumanPrincipalSourceFence>(),
                () => provider.GetRequiredService<IHumanAuthorizationRouteSource>(),
                () => provider.GetRequiredService<IHumanSupportAccessSourceFactory>(),
                () => provider.GetRequiredService<IPolicySourceSetFence>(),
                () => provider.GetRequiredService<IHumanActionCommitFinalizer>(),
                () => provider.GetRequiredService<IHumanManagedProviderProvisioningDataSource>()));
        services.AddScoped<HumanManagedProviderProvisioningCoordinator>(provider =>
        {
            var authority = provider.GetRequiredService<IPrivacyIdentityFenceAuthority>();
            var owner = authority as IOriginalOwnerSettlementBoundary
                ?? provider.GetRequiredService<IOriginalOwnerSettlementBoundary>();
            return new HumanManagedProviderProvisioningCoordinator(
                provider.GetRequiredService<IHumanCommitScopePreparation>(),
                provider.GetRequiredService<IHumanManagedProviderProvisioningPreparation>(),
                provider.GetRequiredService<PrivacyIdentityFenceOperation>(),
                owner,
                provider.GetRequiredService<ISettingMutationLock>(),
                provider.GetRequiredService<IOptions<ManagedControlPlaneOptions>>());
        });
        return services;
    }
}
