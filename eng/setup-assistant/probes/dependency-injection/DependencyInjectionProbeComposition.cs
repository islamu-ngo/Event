namespace ISLAMU.Event.SetupAssistant.Probes.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;

internal static class DependencyInjectionProbeComposition
{
    internal static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddTransient<DependencyInjectionProbeService>();
        return services.BuildServiceProvider();
    }
}

internal sealed class DependencyInjectionProbeService;
