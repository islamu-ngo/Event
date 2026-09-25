using Explore.Blazor.Extensions;
using Explore.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;

namespace Event.Standalone.Hosting;

public static class CombinedApiDataProtectionExtensions
{
    /// <summary>Keep the API database keyring authoritative after optional BFF Redis registration.</summary>
    public static IServiceCollection AddCombinedApiDataProtection(this IServiceCollection services)
    {
        // OpenAPI/test hosts without the API database leave their existing BFF key authority intact.
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(DataProtectionKeyContext)))
            return services;
        services.AddDataProtection()
            .SetApplicationName(BffDataProtectionExtensions.ApplicationName)
            .PersistKeysToDbContext<DataProtectionKeyContext>();
        return services;
    }
}
