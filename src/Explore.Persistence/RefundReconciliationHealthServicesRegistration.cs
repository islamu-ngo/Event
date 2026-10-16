using Explore.Persistence.Services;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence;

public static class RefundReconciliationHealthServicesRegistration
{
    /// <summary>Requires the independently configured IGlobalExploreDbContextFactory.</summary>
    public static IServiceCollection AddRefundReconciliationHealthPersistence(this IServiceCollection services) =>
        services.AddScoped<IRefundReconciliationHealthObserver, RefundReconciliationHealthObserver>();
}
