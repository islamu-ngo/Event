using Explore.Persistence.Services;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Persistence;

public static class InventoryHoldExpiryServicesRegistration
{
    /// <summary>Call after the independent Global/Runtime factories and selected scoped context registration.</summary>
    public static IServiceCollection AddInventoryHoldExpiryWorker(this IServiceCollection services)
    {
        services.TryAddScoped<InventoryHoldExpiryExecutionLease>();
        services.TryAddScoped<IInventoryHoldExpiryDiscovery>(provider => new InventoryHoldExpiryDiscovery(
            provider.GetRequiredService<IGlobalExploreDbContextFactory>(),
            provider.GetService<PostgresNativeQueueEnrollment>()));
        services.TryAddScoped<IInventoryHoldExpiryExecutionScopeFactory, InventoryHoldExpiryExecutionScopeFactory>();
        return services;
    }
}
