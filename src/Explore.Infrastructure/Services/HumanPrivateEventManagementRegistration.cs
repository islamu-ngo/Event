using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services;

internal static class HumanPrivateEventManagementRegistration
{
    internal static IServiceCollection AddHumanPrivateEventManagement(this IServiceCollection services)
    {
        services.AddScoped<HumanPrivateEventManagementReader>(provider => new HumanPrivateEventManagementReader(
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
            () => provider.GetRequiredService<IHumanEventAssignmentSourceFactory>(),
            () => provider.GetRequiredService<IHumanSupportAccessSourceFactory>(),
            () => provider.GetRequiredService<IPolicySourceSetFence>(),
            () => provider.GetRequiredService<ISerializableAttemptOwner>(),
            () => provider.GetRequiredService<IHumanActionCommitFinalizer>(),
            () => provider.GetRequiredService<IPrivateEventManagementRepository>(),
            () => provider.GetRequiredService<IHumanEventTeamActionPreparation>()));
        services.AddScoped<IPrivateEventManagementReader>(provider => provider.GetRequiredService<HumanPrivateEventManagementReader>());
        services.AddScoped<IPrivateEventManagementReadCustody>(provider => provider.GetRequiredService<HumanPrivateEventManagementReader>());
        services.AddScoped<Func<IPrivateEventManagementReadCustody>>(provider =>
            () => provider.GetRequiredService<IPrivateEventManagementReadCustody>());
        services.AddScoped<Func<IPrivateEventManagementReader>>(provider =>
            () => provider.GetRequiredService<IPrivateEventManagementReader>());
        return services;
    }
}
