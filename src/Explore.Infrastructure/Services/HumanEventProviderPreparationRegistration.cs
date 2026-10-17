using Explore.Application.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services;

/// <summary>Opt-in composition only; no handler, host, database authority or provider is replaced.</summary>
public static class HumanEventProviderPreparationRegistration
{
    public static IServiceCollection AddHumanEventProviderMutationPreparation(this IServiceCollection services)
    {
        services.AddScoped<IHumanEventProviderMutationPreparation>(provider => new HumanEventProviderMutationPreparation(
            provider.GetRequiredService<IHumanCommitScopePreparation>(),
            provider.GetRequiredService<IHttpContextAccessor>(),
            provider.GetRequiredService<IHumanAuthenticationLifetimeObserver>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<CerbosAuthorizationService>(),
            provider.GetRequiredService<IOptions<AuthorizationProviderDeploymentOptions>>(),
            provider.GetRequiredService<IOptions<CerbosSettings>>(),
            () => provider.GetRequiredService<IHumanPrincipalSourceFence>(),
            () => provider.GetRequiredService<IHumanAuthorizationRouteSource>(),
            () => provider.GetRequiredService<IHumanEventAssignmentSourceFactory>(),
            () => provider.GetRequiredService<IHumanSupportAccessSourceFactory>(),
            () => provider.GetRequiredService<IPolicySourceSetFence>(),
            () => provider.GetRequiredService<IHumanActionCommitFinalizer>(),
            () => provider.GetRequiredService<ISerializableAttemptOwner>(),
            () => provider.GetRequiredService<IHumanEventProviderSourceFactory>()));
        return services;
    }
}
