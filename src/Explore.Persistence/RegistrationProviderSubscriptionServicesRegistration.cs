using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Registration;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Persistence;

public static class RegistrationProviderSubscriptionServicesRegistration
{
    /// <summary>Call after the ordinary Global/Runtime factories and selected scoped context are registered.</summary>
    public static IServiceCollection AddRegistrationProviderSubscriptionWorker(this IServiceCollection services)
    {
        services.TryAddScoped<ExploreDbContextExecutionLease>();
        services.TryAddScoped<IRegistrationProviderSubscriptionClaimQueue, RegistrationProviderSubscriptionClaimQueue>();
        services.TryAddScoped<IRegistrationProviderSubscriptionExecutionScopeFactory, RegistrationProviderSubscriptionExecutionScopeFactory>();
        services.TryAddScoped<IRegistrationProviderSubscriptionStateRepository, RegistrationProviderSubscriptionStateRepository>();
        services.TryAddScoped<IRegistrationProviderSubscriptionCustodyRepository, RegistrationProviderSubscriptionStateRepository>();
        services.TryAddScoped<RegistrationProviderSubscriptionLifecycleService>();
        services.TryAddScoped<RegistrationProviderSubscriptionDrainService>();
        return services;
    }
}
