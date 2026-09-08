using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Event.Web.BffHosting.Authentication;

public sealed class EventBffKeycloakAuthenticationOptions
{
    public const string SectionName = "Bff:Authentication";
    private const string KeycloakSectionName = "Keycloak";
    private const string BffSectionName = "Bff";
    private const string CookieSectionName = "Cookie";
    private const string SecuritySectionName = "Security";

    public string? Authority { get; init; }

    public string? MetadataAddress { get; init; }

    public string ClientId { get; init; } = string.Empty;

    public string? ClientSecret { get; init; }

    public bool? RequireHttpsMetadata { get; init; }

    public string CallbackPath { get; init; } = "/signin-oidc";

    public string SignedOutCallbackPath { get; init; } = "/signout-callback-oidc";

    public string LoginPath { get; init; } = "/login";

    public string AccessDeniedPath { get; init; } = "/forbidden";

    public string CookieName { get; init; } = string.Empty;

    public int CookieLifetimeMinutes { get; init; } = 12 * 60;

    public TimeSpan CookieLifetime => TimeSpan.FromMinutes(CookieLifetimeMinutes);

    public bool RequireInstanceAdminClaim { get; init; } = true;

    public string InstanceAdminClaimType { get; init; } = "explore:admin:instance";

    public string InstanceAdminClaimValue { get; init; } = "true";

    public static EventBffKeycloakAuthenticationOptions FromConfiguration(
        IConfiguration configuration,
        EventBffHostProfile profile,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var isPrivateHost = profile == EventBffHostProfile.ControlPlane;
        var authentication = configuration.GetSection(SectionName);
        var keycloak = configuration.GetSection(KeycloakSectionName);
        var bff = configuration.GetSection(BffSectionName);
        var cookie = bff.GetSection(CookieSectionName);
        var security = bff.GetSection(SecuritySectionName);

        return new EventBffKeycloakAuthenticationOptions
        {
            Authority = Select(isPrivateHost,
                First(authentication, nameof(Authority)),
                First(keycloak, nameof(KeycloakFallback.Authority))),
            MetadataAddress = Select(isPrivateHost,
                First(authentication, nameof(MetadataAddress)),
                First(keycloak, nameof(KeycloakFallback.MetadataAddress))),
            ClientId = Select(isPrivateHost,
                    First(authentication, nameof(ClientId)),
                    First(keycloak, nameof(KeycloakFallback.ClientId)))
                ?? DefaultClientId(profile),
            ClientSecret = Select(isPrivateHost,
                First(authentication, nameof(ClientSecret)),
                First(keycloak, nameof(KeycloakFallback.ClientSecret))),
            RequireHttpsMetadata = Select(isPrivateHost,
                Bool(authentication, nameof(RequireHttpsMetadata)),
                Bool(keycloak, nameof(KeycloakFallback.RequireHttpsMetadata))),
            CallbackPath = First(authentication, nameof(CallbackPath)) ?? "/signin-oidc",
            SignedOutCallbackPath = First(authentication, nameof(SignedOutCallbackPath))
                ?? "/signout-callback-oidc",
            LoginPath = First(authentication, nameof(LoginPath))
                ?? First(cookie, nameof(CookieFallback.LoginPath))
                ?? "/auth/login",
            AccessDeniedPath = First(authentication, nameof(AccessDeniedPath))
                ?? First(cookie, nameof(CookieFallback.AccessDeniedPath))
                ?? "/forbidden",
            CookieName = First(authentication, nameof(CookieName))
                ?? First(cookie, nameof(CookieFallback.Name))
                ?? DefaultCookieName(profile, environment),
            CookieLifetimeMinutes = PositiveInt(authentication, nameof(CookieLifetimeMinutes))
                ?? PositiveInt(cookie, nameof(CookieFallback.LifetimeMinutes))
                ?? (profile == EventBffHostProfile.ControlPlane ? 8 * 60 : 12 * 60),
            RequireInstanceAdminClaim = Bool(authentication, nameof(RequireInstanceAdminClaim))
                ?? Bool(security, nameof(SecurityFallback.RequireInstanceAdminClaim))
                ?? profile == EventBffHostProfile.ControlPlane,
            InstanceAdminClaimType = First(authentication, nameof(InstanceAdminClaimType))
                ?? First(security, nameof(SecurityFallback.InstanceAdminClaimType))
                ?? "explore:admin:instance",
            InstanceAdminClaimValue = First(authentication, nameof(InstanceAdminClaimValue))
                ?? First(security, nameof(SecurityFallback.InstanceAdminClaimValue))
                ?? "true"
        };
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Authority) && string.IsNullOrWhiteSpace(MetadataAddress))
        {
            throw new InvalidOperationException(
                "Keycloak OIDC configuration is missing. Configure Bff:Authentication:Authority or Bff:Authentication:MetadataAddress.");
        }

        if (string.IsNullOrWhiteSpace(ClientId))
        {
            throw new InvalidOperationException(
                "Keycloak OIDC configuration is missing Bff:Authentication:ClientId.");
        }

        if (string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new InvalidOperationException(
                "Keycloak OIDC configuration is missing Bff:Authentication:ClientSecret for the confidential BFF client.");
        }
    }

    private static T? Select<T>(bool privateHost, T? ownValue, T? publicFallback) where T : struct =>
        ownValue ?? (privateHost ? null : publicFallback);

    private static string? Select(bool privateHost, string? ownValue, string? publicFallback) =>
        ownValue ?? (privateHost ? null : publicFallback);

    private static string DefaultClientId(EventBffHostProfile profile) =>
        profile == EventBffHostProfile.ControlPlane
            ? string.Empty
            : "islamu-event-blazor";

    private static string DefaultCookieName(EventBffHostProfile profile, IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            return profile == EventBffHostProfile.ControlPlane
                ? "event-control-plane-bff-dev"
                : "islamu-event-public-dev";
        }

        return profile == EventBffHostProfile.ControlPlane
            ? "__Host-event-control-plane-bff"
            : "__Host-islamu-event-web";
    }

    private static string? First(IConfiguration section, string childName)
    {
        var value = section[childName];
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool? Bool(IConfiguration section, string childName)
    {
        var value = First(section, childName);
        return bool.TryParse(value, out var parsed) ? parsed : null;
    }

    private static int? PositiveInt(IConfiguration section, string childName)
    {
        var value = First(section, childName);
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : null;
    }

    private sealed class KeycloakFallback
    {
        public string? Authority { get; init; }
        public string? MetadataAddress { get; init; }
        public string? ClientId { get; init; }
        public string? ClientSecret { get; init; }
        public bool? RequireHttpsMetadata { get; init; }
    }

    private sealed class CookieFallback
    {
        public string? Name { get; init; }
        public string? LoginPath { get; init; }
        public string? AccessDeniedPath { get; init; }
        public int? LifetimeMinutes { get; init; }
    }

    private sealed class SecurityFallback
    {
        public bool? RequireInstanceAdminClaim { get; init; }
        public string? InstanceAdminClaimType { get; init; }
        public string? InstanceAdminClaimValue { get; init; }
    }
}
