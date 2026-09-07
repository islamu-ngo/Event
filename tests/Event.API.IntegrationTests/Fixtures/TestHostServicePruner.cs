using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Fixtures;

internal static class TestHostServicePruner
{
    public static void RemoveNoisyHostedServices(
        IServiceCollection services,
        bool removeJwtAuthorityWarmup = true)
    {
        RemoveHostedService(services, "OpenFeature.Hosting.HostedFeatureLifecycleService");

        if (removeJwtAuthorityWarmup)
        {
            RemoveHostedService(services, "Explore.API.Authentication.JwtAuthorityWarmupHostedService");
        }
    }

    private static void RemoveHostedService(IServiceCollection services, string implementationTypeName)
    {
        for (var index = services.Count - 1; index >= 0; index--)
        {
            var descriptor = services[index];
            if (string.Equals(
                descriptor.ImplementationType?.FullName,
                implementationTypeName,
                StringComparison.Ordinal))
            {
                services.RemoveAt(index);
            }
        }
    }
}
