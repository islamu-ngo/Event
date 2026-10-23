using Explore.Persistence.Services;
using Explore.Application.Contracts.Recovery;
using Explore.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence;

public static class TicketingRecoveryHealthServicesRegistration
{
    public static IServiceCollection AddTicketingRecoveryHealthPersistence(this IServiceCollection services) =>
        services.AddScoped<ITicketingRecoveryHealthObserver, TicketingRecoveryHealthObserver>();
}
