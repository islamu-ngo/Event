using Microsoft.AspNetCore.DataProtection;
using StackExchange.Redis;

namespace Explore.Blazor.Extensions;

public static class BffDataProtectionExtensions
{
    public const string ApplicationName = "islamu-event";
    public const string KeyRingName = "islamu-event:data-protection-keys";

    public static IServiceCollection AddBffDataProtection(
        this IServiceCollection services,
        string? redisConnectionString)
    {
        var dataProtection = services
            .AddDataProtection()
            .SetApplicationName(ApplicationName);

        if (string.IsNullOrWhiteSpace(redisConnectionString))
        {
            return services;
        }

        var connection = new Lazy<IConnectionMultiplexer>(
            () => ConnectionMultiplexer.Connect(redisConnectionString));
        services.AddSingleton(_ => connection.Value);

        dataProtection.PersistKeysToStackExchangeRedis(
            () => connection.Value.GetDatabase(),
            KeyRingName);

        return services;
    }

}
