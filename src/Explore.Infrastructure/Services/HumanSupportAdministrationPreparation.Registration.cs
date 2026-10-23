using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services;

public static class HumanSupportAdministrationPreparationRegistration
{
    public static IServiceCollection AddHumanSupportAdministrationPreparation(this IServiceCollection services)
    {
        services.AddScoped<IHumanSupportAdministrationPreparation>(provider => new HumanSupportAdministrationPreparation(
            provider.GetRequiredService<IHumanCommitScopePreparation>(),
            provider.GetRequiredService<IHttpContextAccessor>(),
            provider.GetRequiredService<IHumanAuthenticationLifetimeObserver>(),
            provider.GetRequiredService<IMachinePrincipalAccessor>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<CerbosAuthorizationService>(),
            provider.GetRequiredService<IOptions<AuthorizationProviderDeploymentOptions>>(),
            provider.GetRequiredService<IOptions<CerbosSettings>>(),
            () => provider.GetRequiredService<IHumanPrincipalSourceFence>(),
            () => provider.GetRequiredService<IHumanAuthorizationRouteSource>(),
            () => provider.GetRequiredService<IHumanSupportAccessSourceFactory>(),
            () => provider.GetRequiredService<IPolicySourceSetFence>(),
            () => provider.GetRequiredService<ISupportAdministrationSource>(),
            () => provider.GetRequiredService<IHumanActionCommitFinalizer>()));
        return services;
    }
}
