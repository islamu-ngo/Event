using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Event.Web.BffHosting.Authentication;

public static class EventBffAuthenticationExtensions
{
    public static IServiceCollection AddEventBffKeycloakAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        EventBffHostProfile hostProfile)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var authOptions = EventBffKeycloakAuthenticationOptions.FromConfiguration(
            configuration,
            hostProfile,
            environment);
        authOptions.Validate();

        services.TryAddScoped<EventBffTokenRefreshCookieEvents>();
        services.AddSingleton(authOptions);

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                options.Cookie.Name = authOptions.CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = environment.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.ExpireTimeSpan = authOptions.CookieLifetime;
                options.SlidingExpiration = true;
                options.LoginPath = authOptions.LoginPath;
                options.AccessDeniedPath = authOptions.AccessDeniedPath;
                options.EventsType = typeof(EventBffTokenRefreshCookieEvents);
            })
            .AddOpenIdConnect(EventBffAuthenticationSchemes.Keycloak, options =>
            {
                ConfigureKeycloakOptions(options, authOptions, environment);
            });

        var authorization = services.AddAuthorizationBuilder();
        authorization.AddPolicy(EventBffAuthorizationPolicies.ControlPlaneAccess, policy =>
        {
            policy.RequireAuthenticatedUser();
            if (authOptions.RequireInstanceAdminClaim)
            {
                policy.RequireClaim(authOptions.InstanceAdminClaimType, authOptions.InstanceAdminClaimValue);
            }
        });

        return services;
    }

    private static void ConfigureKeycloakOptions(
        OpenIdConnectOptions options,
        EventBffKeycloakAuthenticationOptions authOptions,
        IWebHostEnvironment environment)
    {
        options.Authority = authOptions.Authority;
        options.MetadataAddress = authOptions.MetadataAddress;
        options.ClientId = authOptions.ClientId;
        options.ClientSecret = authOptions.ClientSecret?.Trim() ?? string.Empty;
        options.UsePkce = true;
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.RequireHttpsMetadata = authOptions.RequireHttpsMetadata ?? !environment.IsDevelopment();
        options.CallbackPath = authOptions.CallbackPath;
        options.SignedOutCallbackPath = authOptions.SignedOutCallbackPath;
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.ResponseMode = OpenIdConnectResponseMode.Query;
        options.PushedAuthorizationBehavior = PushedAuthorizationBehavior.Disable;
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.NonceCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.NonceCookie.SecurePolicy = environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "preferred_username"
        };
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.BackchannelHttpHandler = EventBffOidcOptionsFactory.CreateIpv4BackchannelHandler();
        options.Events = CreateEvents();
    }

    private static OpenIdConnectEvents CreateEvents()
    {
        return new OpenIdConnectEvents
        {
            OnTokenResponseReceived = context =>
            {
                context.Properties?.Items[EventBffAuthenticationConstants.OidcSchemePropertyKey] =
                    context.Scheme.Name;
                return Task.CompletedTask;
            },
            OnRemoteFailure = context =>
            {
                var diagnostics = context.HttpContext.RequestServices
                    .GetRequiredService<ISafeAuthDiagnosticsPolicy>();
                var diagnostic = diagnostics.CreateDiagnostic(
                    "oidc_remote_failure",
                    context.Failure);

                var returnUrl = context.Properties?.RedirectUri ?? "/";
                var redirectUrl = diagnostics.BuildLoginRedirectUrl(
                    returnUrl,
                    "keycloak",
                    diagnostic);

                context.Response.Redirect(redirectUrl);
                context.HandleResponse();
                return Task.CompletedTask;
            }
        };
    }
}
