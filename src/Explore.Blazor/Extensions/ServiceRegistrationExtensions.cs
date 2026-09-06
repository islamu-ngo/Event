// ABOUTME: Registers server-specific services on top of the shared application services.
// ABOUTME: Shared services live in Explore.Blazor.Client.Extensions.ServiceCollectionExtensions.

using CarpaNet.OAuth.Storage;
using Explore.Blazor.Authentication;
using Explore.Blazor.Client.Configuration;
using Explore.Blazor.Client.Contracts.Interop;
using Explore.Blazor.Client.Contracts.Services.Events;
using Explore.Blazor.Client.Contracts.Services.Organizations;
using Explore.Blazor.Client.Extensions;
using Explore.Blazor.Client.Routing.Guards;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Services.Http;
using Explore.Blazor.Services;
using Explore.Blazor.Services.Auth;
using Explore.Blazor.Services.Preferences;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Explore.Blazor.Extensions;

public static class ServiceRegistrationExtensions
{
    /// <summary>
    /// Registers all application-level services by calling the shared registrations
    /// from the Client project, then adding server-specific overrides.
    /// Shared UI HTTP services use same-origin BFF Refit endpoints in both server and WASM hosts.
    /// </summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddTransient<BrowserCredentialsMessageHandler>();
        services.AddTransient<BffAntiforgeryMessageHandler>();
        services.AddTransient<BffUnauthorizedHandler>();

        services.AddSharedApplicationServices(
            ConfigureBffRefitBaseAddress,
            builder => builder.AddHttpMessageHandler<BffCookieForwardingHandler>());
        services.AddScoped<BffClient>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var navigation = sp.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
            var http = factory.CreateClient("BffSelfClient");
            http.BaseAddress = new Uri(navigation.BaseUri);
            return new BffClient(http, sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>());
        });
        services.AddScoped<IBffClient>(sp => sp.GetRequiredService<BffClient>());
        services.AddScoped<AuthenticatedRouteGuard>();
        services.AddScoped<MultiTenantOnboardingRouteGuard>();
        services.AddScoped<AdminRouteGuard>();
        services.AddScoped<TenantAdminRouteGuard>();
        services.AddScoped<OrgAdminRouteGuard>();
        services.AddScoped<GroupAdminRouteGuard>();

        return services;
    }

    private static void ConfigureBffRefitBaseAddress(IServiceProvider serviceProvider, HttpClient client)
    {
        var httpContextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
        var request = httpContextAccessor.HttpContext?.Request;

        if (request is null)
        {
            return;
        }

        var pathBase = request.PathBase.HasValue ? request.PathBase.Value : string.Empty;
        client.BaseAddress = new Uri($"{request.Scheme}://{request.Host}{pathBase}/", UriKind.Absolute);
    }


    /// <summary>
    /// Registers services that are only needed on the Blazor Server (BFF) side.
    /// These include token management, analytics no-ops, and admin claims enrichment.
    /// </summary>
    public static IServiceCollection AddServerOnlyServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Server-specific IAnalyticsInterop (no-op, replaces shared registration)
        services.AddScoped<IAnalyticsInterop, ServerAnalyticsInterop>();
        services.AddScoped<ICookieConsentInterop, ServerCookieConsentInterop>();
        services.AddScoped<Explore.Blazor.Client.Services.CookieConsentStateService>();
        services.AddScoped<ICircuitUserContext, CircuitUserContext>();
        services.AddScoped<IBffAuthCookieStore, BffAuthCookieStore>();
        services.AddSingleton<ICircuitTokenStore, CircuitTokenStore>();
        services.AddScoped<ICircuitAccessTokenService, CircuitAccessTokenService>();
        services.AddSingleton<SetupSecretSessionService>();
        services.AddSingleton<ISetupSecretSessionService>(sp => sp.GetRequiredService<SetupSecretSessionService>());
        services.Configure<SetupSecretResolverOptions>(options =>
        {
            options.DevelopmentSecret = configuration["Setup:Secret"]?.Trim()
                ?? configuration["Explore:Setup:Secret"]?.Trim()
                ?? configuration["SETUP_SECRET"]?.Trim();
        });
        services.AddSingleton<ISetupSecretCookieProtector, SetupSecretCookieProtector>();
        services.AddScoped<ISetupSecretResolver, SetupSecretResolver>();
        services.AddScoped<IStorageUploadSessionStore, StorageUploadSessionStore>();
        services.AddScoped<IBffSupportAccessSessionStore, BffSupportAccessSessionStore>();
        services.AddSingleton<IBffSelfCallTokenService, BffSelfCallTokenService>();
        services.AddSingleton<IBffPreferenceCookieService, BffPreferenceCookieService>();
        services.AddSingleton<IBffPreferenceValidationService, BffPreferenceValidationService>();
        services.AddSingleton<RegistrationPaymentCheckoutTicketStore>();
        services.AddScoped<IBffPreferenceForwardingService, BffPreferenceForwardingService>();
        services.AddMemoryCache();
        RegisterBffDataProtection(services, configuration);
        services.AddScoped<IBffResolverConfigurationProvider, BffResolverConfigurationProvider>();
        services.AddScoped<ITenantRouteContextAccessor, TenantRouteContextAccessor>();
        services.AddScoped<CircuitHandler, TenantCircuitHandler>();
        services.AddScoped<CircuitHandler, TokenCircuitHandler>();
        services.AddSingleton<AdminHostControlPlaneShellSelector>();
        services.Configure<AtprotoAuthenticationOptions>(configuration.GetSection("Atproto"));
        services.Configure<AtprotoClientKeyOptions>(configuration.GetSection("Atproto"));
        services.AddSingleton<AtprotoClientKeyProvider>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AtprotoBootstrapAssertionService>();
        services.AddSingleton<AtprotoTransientAssertionService>();
        services.AddSingleton<ApiBackedAtprotoTransientStore>();
        services.AddSingleton<AtprotoBrowserProof>();
        services.AddSingleton<AtprotoTenantOriginResolver>();
        services.AddScoped<AtprotoOAuthFlowContext>();
        services.AddScoped<ApiBackedOAuthStateStore>();
        services.AddScoped<IOAuthStateStore>(provider => provider.GetRequiredService<ApiBackedOAuthStateStore>());
        services.AddScoped<ApiBackedOAuthSessionStore>();
        services.AddScoped<IOAuthSessionStore>(provider => provider.GetRequiredService<ApiBackedOAuthSessionStore>());
        services.AddSingleton<AtprotoTenantSessionHandoffStore>();
        services.AddSingleton<IAtprotoOAuthTransportFactory, AtprotoOAuthTransportFactory>();
        services.AddSingleton<AtprotoOAuthClientFactory>();

        // BFF admin claims enrichment — invoked at cookie/session boundaries, not per request.
        services.AddScoped<BffAdminClaimsTransformation>();

        services.AddSingleton<IBffOnboardingStatusProvider, BffOnboardingStatusProvider>();

        // Multi-tenancy configuration
        services.Configure<TenantConfiguration>(
            configuration.GetSection("Explore:MultiTenancy"));

        return services;
    }

    private static void RegisterBffDataProtection(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("cache");
        services.AddBffDataProtection(connectionString);
    }
}
