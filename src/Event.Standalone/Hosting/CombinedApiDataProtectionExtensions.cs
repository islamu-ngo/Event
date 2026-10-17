using Explore.ServiceDefaults.DataProtection;

namespace Event.Standalone.Hosting;

public static class CombinedApiDataProtectionExtensions
{
    /// <summary>Select the deliberate Combined partition after API and BFF service composition.</summary>
    public static IServiceCollection AddCombinedApiDataProtection(this IServiceCollection services)
    {
        return services.AddExternalDataProtection("combined");
    }
}
