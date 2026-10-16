using Explore.Application.Contracts.Infrastructure;
using Explore.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Infrastructure;

public static class HierarchicalSettingsReaderServicesRegistration
{
    /// <summary>
    /// Registers a scoped reader independently of human settings mutation services.
    /// The host must supply the setting repositories and shared memory cache.
    /// </summary>
    public static IServiceCollection AddHierarchicalSettingsReader(this IServiceCollection services)
    {
        services.TryAddScoped<IHierarchicalSettingsReader, HierarchicalSettingsReader>();
        return services;
    }
}
