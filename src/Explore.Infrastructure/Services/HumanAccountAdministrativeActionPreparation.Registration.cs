using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services;

public static class HumanAccountAdministrativeActionPreparationRegistration
{
    public static IServiceCollection AddHumanAccountAdministrativeActionPreparation(this IServiceCollection services)
    {
        services.AddScoped<IHumanAccountAdministrativeActionPreparation>(provider =>
            new HumanAccountAdministrativeActionPreparation(
                provider.GetRequiredService<IHumanCommitScopePreparation>(),
                provider.GetRequiredService<IHttpContextAccessor>(),
                provider.GetRequiredService<IHumanAuthenticationLifetimeObserver>(),
                provider.GetRequiredService<IMachinePrincipalAccessor>(),
                provider.GetRequiredService<ITenantContext>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<CerbosAuthorizationService>(),
                provider.GetRequiredService<IOptions<AuthorizationProviderDeploymentOptions>>(),
                provider.GetRequiredService<IOptions<CerbosSettings>>(),
                () => provider.GetRequiredService<AuthorizationResourceContextResolver>(),
                () => provider.GetRequiredService<IHumanPrincipalSourceFence>(),
                () => provider.GetRequiredService<IHumanAuthorizationRouteSource>(),
                () => provider.GetRequiredService<IHumanSupportAccessSourceFactory>(),
                () => provider.GetRequiredService<IPolicySourceSetFence>(),
                () => provider.GetRequiredService<IAccountMembershipPolicySource>(),
                () => provider.GetRequiredService<IHumanActionCommitFinalizer>()));
        return services;
    }
}
