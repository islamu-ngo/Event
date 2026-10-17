using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Infrastructure.Services;

internal static class RegistrationPrivateReadRegistration
{
    internal static IServiceCollection AddRegistrationPrivateReads(this IServiceCollection services)
    {
        services.AddScoped<RegistrationPrivateReader>(provider => new RegistrationPrivateReader(
            provider.GetRequiredService<IHumanCommitScopePreparation>(),
            provider.GetRequiredService<IHttpContextAccessor>(),
            provider.GetRequiredService<IHumanAuthenticationLifetimeObserver>(),
            provider.GetRequiredService<IMachinePrincipalAccessor>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<TimeProvider>(),
            () => provider.GetRequiredService<IAuthorizationProvider>(),
            () => provider.GetRequiredService<IHumanPrincipalSourceFence>(),
            () => provider.GetRequiredService<IHumanAuthorizationRouteSource>(),
            () => provider.GetRequiredService<IHumanEventAssignmentSourceFactory>(),
            () => provider.GetRequiredService<IHumanSupportAccessSourceFactory>(),
            () => provider.GetRequiredService<IPolicySourceSetFence>(),
            () => provider.GetRequiredService<ISerializableAttemptOwner>(),
            () => provider.GetRequiredService<IHumanActionCommitFinalizer>(),
            () => provider.GetRequiredService<IRegistrationPrivateReadRepository>()));
        services.AddScoped<IRegistrationPrivateReader>(provider => provider.GetRequiredService<RegistrationPrivateReader>());
        services.AddScoped<IRegistrationPrivateReadCustody>(provider => provider.GetRequiredService<RegistrationPrivateReader>());
        return services;
    }
}
