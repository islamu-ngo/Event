using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.AiAssistant.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services;

public static class HumanAiModerationProposalRegistration
{
    public static IServiceCollection AddHumanAiModerationProposalPreparation(this IServiceCollection services)
    {
        services.AddScoped<IHumanAiToolProposalPreparation>(provider => new HumanAiToolProposalPreparation(
            provider.GetRequiredService<IHumanCommitScopePreparation>(),
            provider.GetRequiredService<IHttpContextAccessor>(),
            provider.GetRequiredService<IHumanAuthenticationLifetimeObserver>(),
            provider.GetRequiredService<IMachinePrincipalAccessor>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<IAiToolContractRegistry>(),
            provider.GetRequiredService<IEventRepository>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<CerbosAuthorizationService>(),
            provider.GetRequiredService<IOptions<AuthorizationProviderDeploymentOptions>>(),
            provider.GetRequiredService<IOptions<CerbosSettings>>(),
            () => provider.GetRequiredService<IHumanPrincipalSourceFence>(),
            () => provider.GetRequiredService<IHumanAuthorizationRouteSource>(),
            () => provider.GetRequiredService<IHumanEventAssignmentSourceFactory>(),
            () => provider.GetRequiredService<IHumanSupportAccessSourceFactory>(),
            () => provider.GetRequiredService<IPolicySourceSetFence>(),
            () => provider.GetRequiredService<IHumanActionCommitFinalizer>()));
        services.AddScoped<IHumanAiModerationProposalPreparation>(provider => new HumanAiModerationProposalPreparation(
            provider.GetRequiredService<IHumanCommitScopePreparation>(),
            provider.GetRequiredService<IHttpContextAccessor>(),
            provider.GetRequiredService<IHumanAuthenticationLifetimeObserver>(),
            provider.GetRequiredService<IMachinePrincipalAccessor>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<IAiToolContractRegistry>(),
            provider.GetRequiredService<IEventRepository>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<CerbosAuthorizationService>(),
            provider.GetRequiredService<IOptions<AuthorizationProviderDeploymentOptions>>(),
            provider.GetRequiredService<IOptions<CerbosSettings>>(),
            () => provider.GetRequiredService<IHumanPrincipalSourceFence>(),
            () => provider.GetRequiredService<IHumanAuthorizationRouteSource>(),
            () => provider.GetRequiredService<IHumanAiModerationProposalTargetSourceFactory>(),
            () => provider.GetRequiredService<IHumanSupportAccessSourceFactory>(),
            () => provider.GetRequiredService<IPolicySourceSetFence>(),
            () => provider.GetRequiredService<IHumanActionCommitFinalizer>()));
        return services;
    }
}
