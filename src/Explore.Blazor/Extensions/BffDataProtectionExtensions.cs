using Explore.ServiceDefaults.DataProtection;

namespace Explore.Blazor.Extensions;

public static class BffDataProtectionExtensions
{
    public const string ApplicationName = "islamu-event:bff:v1";

    public static IServiceCollection AddBffDataProtection(
        this IServiceCollection services)
    {
        return services.AddExternalDataProtection("bff");
    }

}
