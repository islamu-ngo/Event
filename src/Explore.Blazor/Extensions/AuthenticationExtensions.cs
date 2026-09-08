using Event.Web.BffHosting.Authentication;
using Explore.Blazor.Authentication;
using Explore.Blazor.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Serilog;

namespace Explore.Blazor.Extensions;

public static class AuthenticationExtensions
{
    /// <summary>
    /// Configures BFF authentication with Cookie scheme (always present) and registers
    /// the <see cref="DynamicAuthSchemeManager"/> for runtime OIDC/OAuth scheme registration.
    /// Provider-specific schemes (Keycloak, Google, ATProto) are registered dynamically
    /// from database configuration and environment variables after the app starts.
    /// </summary>
    public static IServiceCollection AddBffAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        LogEnvironmentKeycloakStatus(configuration);

        services.AddAuthentication(options =>
            {
                // Cookie is always the default scheme (reads session from cookie).
                // DefaultChallengeScheme is also Cookie — its LoginPath="/login" redirects
                // unauthenticated users to the multi-provider login page.
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                options.LoginPath = "/login";
                options.LogoutPath = "/logout";
                options.ExpireTimeSpan = TimeSpan.FromDays(180);
                options.SlidingExpiration = true;

                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = environment.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;

                options.EventsType = typeof(EventBffTokenRefreshCookieEvents);
            });

        services.AddScoped<IEventBffCookieSessionHandler, ExploreBffCookieSessionHandler>();
        services.TryAddScoped<EventBffTokenRefreshCookieEvents>();

        // Register the ATProto auth handler options so the scheme can be added dynamically.
        // The handler itself is registered by DynamicAuthSchemeManager when ATProto is enabled.
        services.AddOptions<AtprotoAuthenticationOptions>(Explore.Blazor.Constants.AuthSchemeNames.Atproto)
            .Bind(configuration.GetSection("Atproto"));

        // Register OpenIdConnectHandler and its PostConfigure in DI so that dynamically
        // added OIDC schemes (Keycloak, Google) can be resolved at runtime.
        // Normally AddOpenIdConnect() does this, but we register schemes dynamically via
        // IAuthenticationSchemeProvider + IOptionsMonitorCache, so we need the handler
        // and PostConfigure available without calling AddOpenIdConnect().
        services.AddTransient<OpenIdConnectHandler>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPostConfigureOptions<OpenIdConnectOptions>,
                OpenIdConnectPostConfigureOptions>());

        // DynamicAuthSchemeManager is a singleton — it holds the set of registered schemes
        // and uses IAuthenticationSchemeProvider + IOptionsMonitorCache for runtime registration.
        services.AddSingleton<Services.Auth.IBffReturnUrlService, Services.Auth.BffReturnUrlService>();
        services.AddSingleton<Services.Auth.AtprotoAuthenticationMetrics>();
        services.AddSingleton<Services.Auth.IBffProviderReadinessService, Services.Auth.BffProviderReadinessService>();
        services.AddSingleton<Services.Auth.IBffAccessTokenAssessmentService, Services.Auth.BffAccessTokenAssessmentService>();
        services.AddOptions<Services.Auth.BffAuthDiagnosticsOptions>()
            .Bind(configuration.GetSection("Keycloak"));
        services.AddScoped<Services.Auth.IBffSessionRefreshService, Services.Auth.BffSessionRefreshService>();
        services.AddScoped<Services.Auth.IBffAuthDiagnosticsService, Services.Auth.BffAuthDiagnosticsService>();
        services.TryAddSingleton<ISafeAuthDiagnosticsPolicy, SafeAuthDiagnosticsPolicy>();
        services.AddSingleton<IDynamicAuthSchemeManager, DynamicAuthSchemeManager>();

        return services;
    }

    /// <summary>
    /// Initializes dynamic auth schemes from database and environment configuration.
    /// Must be called after <c>app.Build()</c> during application startup.
    /// </summary>
    public static async Task InitializeDynamicAuthSchemesAsync(this WebApplication app)
    {
        var schemeManager = app.Services.GetRequiredService<IDynamicAuthSchemeManager>();
        await schemeManager.InitializeAsync();
    }

    private static void LogEnvironmentKeycloakStatus(IConfiguration configuration)
    {
        var logger = Log.ForContext("SourceContext", "Startup");

        var authority = configuration["Keycloak:Authority"];
        var clientId = configuration["Keycloak:ClientId"];
        var clientSecret = configuration["Keycloak:ClientSecret"];

        if (!string.IsNullOrEmpty(authority) && !string.IsNullOrEmpty(clientId))
        {
            logger.Information(
                "Keycloak environment config detected — Authority: {Authority}, ClientId: {ClientId}, HasSecret: {HasSecret}. " +
                "Will be registered as auth scheme during initialization.",
                authority, clientId, !string.IsNullOrEmpty(clientSecret) ? "YES" : "NO");
        }
        else
        {
            logger.Information(
                "No Keycloak environment config detected — authentication providers will be configured during instance setup.");
        }
    }
}
