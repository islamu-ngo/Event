using Event.Web.BffHosting.Abstractions;
using Event.Web.BffHosting.Authentication;
using Event.Web.BffHosting.Options;
using Event.Web.BffHosting.Proxy;
using Event.Web.BffHosting.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Event.Web.BffHosting.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEventBffHosting(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        EventBffHostProfile hostProfile)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var apiBaseAddress = EventApiBaseAddressResolver.Resolve(configuration);

        services.AddOptions<EventBffHostingOptions>()
            .Bind(configuration.GetSection(EventBffHostingOptions.SectionName))
            .PostConfigure(options =>
            {
                options.HostProfile = hostProfile;
                options.ApiBaseAddress = apiBaseAddress;
            })
            .ValidateOnStart();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<EventBffHostingOptions>, EventBffHostingOptionsValidator>());
        services.TryAddSingleton<IEventBffHostClassifier, EventBffHostClassifier>();
        services.TryAddSingleton<EventBffAdminHostAccessPolicy>();
        services.TryAddSingleton<ISafeAuthDiagnosticsPolicy, SafeAuthDiagnosticsPolicy>();
        services.TryAddSingleton<IEventBffOidcOptionsFactory, EventBffOidcOptionsFactory>();
        services.TryAddScoped<IEventBffCookieSessionHandler, NoopEventBffCookieSessionHandler>();
        services.TryAddScoped<EventBffTokenRefreshCookieEvents>();
        services.AddHttpClient(EventBffTokenRefreshCookieEvents.TokenRefreshHttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        })
            .ConfigurePrimaryHttpMessageHandler(EventBffOidcOptionsFactory.CreateIpv4BackchannelHandler);

        _ = BffDevelopmentHostPolicy.IsDevelopmentTrustedBaseAddress(apiBaseAddress, environment);

        return services;
    }
}
