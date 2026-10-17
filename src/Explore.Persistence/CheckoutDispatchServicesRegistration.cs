using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Persistence;

public static class CheckoutDispatchServicesRegistration
{
    public static IServiceCollection AddCheckoutDispatchPersistence(this IServiceCollection services)
    {
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddScoped<ExploreDbContextExecutionLease>();
        services.TryAddSingleton<ICheckoutDispatchAdmissionDbContextFactory, CheckoutDispatchAdmissionDbContextFactory>();
        services.TryAddScoped<ICheckoutDispatchAdmission, CheckoutDispatchAdmission>();
        services.TryAddScoped<ICheckoutDispatchRuntimeScopeFactory, CheckoutDispatchRuntimeScopeFactory>();
        return services;
    }
}
