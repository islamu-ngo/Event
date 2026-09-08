using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Event.Benchmarks.Api;

internal static class BenchmarkHostedServiceExtensions
{
    private static readonly HashSet<string> ProductHostedServiceNames = new(StringComparer.Ordinal)
    {
        "JwtAuthorityWarmupHostedService",
        "LookupDataCacheInitializer",
        "SecretRefreshService",
        "OutboxProcessor",
        "EmailDispatchProcessor",
        "CerbosPolicyBootSyncWorker"
    };

    public static void RemoveProductHostedServices(this IServiceCollection services)
    {
        for (var index = services.Count - 1; index >= 0; index--)
        {
            var descriptor = services[index];
            if (descriptor.ServiceType != typeof(IHostedService))
            {
                continue;
            }

            var implementationType = descriptor.ImplementationType;
            if (implementationType is not null && ProductHostedServiceNames.Contains(implementationType.Name))
            {
                services.RemoveAt(index);
            }
        }
    }
}
