using Explore.Blazor.Client.Routing.ControlPlane;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Blazor.Client.Extensions;

public static class ControlPlaneServiceCollectionExtensions
{
    public static IServiceCollection AddEventControlPlaneClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IControlPlaneRouteCatalog, ControlPlaneRouteCatalog>();

        return services;
    }

}
