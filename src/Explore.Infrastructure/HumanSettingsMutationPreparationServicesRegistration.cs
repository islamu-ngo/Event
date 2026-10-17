using Explore.Application.Contracts.Persistence;
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
        return services;
    }
}
