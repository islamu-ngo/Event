using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventRoleAssignments.Handlers.Commands;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services;

public static class HumanEventTeamActionPreparationRegistration
{
    public static IServiceCollection AddHumanEventTeamActionPreparation(this IServiceCollection services)
    {
        services.AddHumanAiModerationProposalPreparation();
        services.AddScoped<IHumanEventTeamActionPreparation>(provider => new HumanEventTeamActionPreparation(
            provider.GetRequiredService<IHumanCommitScopePreparation>(),
            provider.GetRequiredService<IHttpContextAccessor>(),
            provider.GetRequiredService<IHumanAuthenticationLifetimeObserver>(),
            provider.GetRequiredService<IMachinePrincipalAccessor>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<IAdminContext>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<CerbosAuthorizationService>(),
            provider.GetRequiredService<IOptions<AuthorizationProviderDeploymentOptions>>(),
            provider.GetRequiredService<IOptions<CerbosSettings>>(),
            () => provider.GetRequiredService<IHumanPrincipalSourceFence>(),
            () => provider.GetRequiredService<IHumanAuthorizationRouteSource>(),
            () => provider.GetRequiredService<IHumanEventAssignmentSourceFactory>(),
            () => provider.GetRequiredService<IHumanSupportAccessSourceFactory>(),
            () => provider.GetRequiredService<IPolicySourceSetFence>(),
            () => provider.GetRequiredService<IEventTeamPolicySource>(),
            () => provider.GetRequiredService<IHumanActionCommitFinalizer>()));
        services.AddScoped<IHumanEventTeamAssignmentOwner, AssignEventRoleCommandHandler>();
        return services;
    }
}
