using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services;

public static class HumanFooterOperationRegistration
{
    public static IServiceCollection AddHumanFooterOperations(this IServiceCollection services)
    {
        services.AddScoped<IFooterOperationExecution>(provider => new HumanFooterOperationExecution(
            provider.GetRequiredService<IHumanCommitScopePreparation>(),
            provider.GetRequiredService<IHttpContextAccessor>(),
            provider.GetRequiredService<IHumanAuthenticationLifetimeObserver>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<CerbosAuthorizationService>(),
            provider.GetRequiredService<IOptions<AuthorizationProviderDeploymentOptions>>(),
            provider.GetRequiredService<IOptions<CerbosSettings>>(),
            provider.GetRequiredService<IMachinePrincipalAccessor>(),
            () => provider.GetRequiredService<IHumanPrincipalSourceFence>(),
            () => provider.GetRequiredService<IHumanAuthorizationRouteSource>(),
            () => provider.GetRequiredService<IHumanSupportAccessSourceFactory>(),
            () => provider.GetRequiredService<IPolicySourceSetFence>(),
            () => provider.GetRequiredService<IHumanActionCommitFinalizer>(),
            () => provider.GetRequiredService<ISerializableAttemptOwner>(),
            () => provider.GetRequiredService<ISettingMutationLock>(),
            () => provider.GetRequiredService<IFooterExecutionScopeFactory>(),
            () => provider.GetRequiredService<IHierarchicalSettingsResolver>(),
            () => provider.GetRequiredService<IFooterSetupReadAuthority>()));
        return services;
    }
}
