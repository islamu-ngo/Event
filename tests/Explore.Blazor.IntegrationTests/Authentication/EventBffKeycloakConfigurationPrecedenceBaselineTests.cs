using Event.Web.BffHosting.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Explore.Blazor.IntegrationTests.Authentication;

[NotInParallel]
public sealed class EventBffKeycloakConfigurationPrecedenceBaselineTests
{
    [Test]
    public async Task PublicWebSectionChildrenOverridePublicFallbacksInRegisteredHandlers()
    {
        var sectionSecret = Guid.CreateVersion7().ToString("N");
        var builder = CreateBuilder(new Dictionary<string, string?>
        {
            ["Bff:Authentication:Authority"] = "https://section.example/realms/events",
            ["Bff:Authentication:ClientId"] = "section-client",
            ["Bff:Authentication:ClientSecret"] = sectionSecret,
            ["Bff:Authentication:RequireHttpsMetadata"] = "false",
            ["Bff:Authentication:LoginPath"] = "/section-login",
            ["Bff:Authentication:CookieLifetimeMinutes"] = "17",
            ["Keycloak:Authority"] = "https://fallback.example/realms/events",
            ["Keycloak:ClientId"] = "fallback-client",
            ["Keycloak:ClientSecret"] = Guid.CreateVersion7().ToString("N"),
            ["Keycloak:RequireHttpsMetadata"] = "true",
            ["Bff:Cookie:LoginPath"] = "/fallback-login",
            ["Bff:Cookie:LifetimeMinutes"] = "41"
        });
        builder.Services.AddEventBffKeycloakAuthentication(
            builder.Configuration, builder.Environment, EventBffHostProfile.PublicWeb);

        await using var provider = builder.Services.BuildServiceProvider();
        var oidc = provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(EventBffAuthenticationSchemes.Keycloak);
        var cookie = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

        await Assert.That(oidc.Authority).IsEqualTo("https://section.example/realms/events");
        await Assert.That(oidc.ClientId).IsEqualTo("section-client");
        await Assert.That(oidc.ClientSecret).IsEqualTo(sectionSecret);
        await Assert.That(oidc.RequireHttpsMetadata).IsFalse();
        await Assert.That(cookie.LoginPath.Value).IsEqualTo("/section-login");
        await Assert.That(cookie.ExpireTimeSpan).IsEqualTo(TimeSpan.FromMinutes(17));
    }

    [Test]
    public async Task PublicWebBlankSectionChildrenUsePublicFallbacksInRegisteredHandlers()
    {
        var fallbackSecret = Guid.CreateVersion7().ToString("N");
        var builder = CreateBuilder(new Dictionary<string, string?>
        {
            ["Bff:Authentication:Authority"] = "   ",
            ["Bff:Authentication:ClientSecret"] = " ",
            ["Keycloak:Authority"] = "https://fallback.example/realms/events",
            ["Keycloak:ClientId"] = "fallback-client",
            ["Keycloak:ClientSecret"] = fallbackSecret,
            ["Keycloak:RequireHttpsMetadata"] = "false",
            ["Bff:Cookie:LoginPath"] = "/cookie-login",
            ["Bff:Cookie:LifetimeMinutes"] = "23"
        });
        builder.Services.AddEventBffKeycloakAuthentication(
            builder.Configuration, builder.Environment, EventBffHostProfile.PublicWeb);

        await using var provider = builder.Services.BuildServiceProvider();
        var oidc = provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(EventBffAuthenticationSchemes.Keycloak);
        var cookie = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

        await Assert.That(oidc.Authority).IsEqualTo("https://fallback.example/realms/events");
        await Assert.That(oidc.ClientId).IsEqualTo("fallback-client");
        await Assert.That(oidc.ClientSecret).IsEqualTo(fallbackSecret);
        await Assert.That(oidc.RequireHttpsMetadata).IsFalse();
        await Assert.That(cookie.LoginPath.Value).IsEqualTo("/cookie-login");
        await Assert.That(cookie.ExpireTimeSpan).IsEqualTo(TimeSpan.FromMinutes(23));
    }

    [Test]
    public async Task EnvironmentSectionChildrenOverrideLowerProviderPublicFallbacks()
    {
        const string clientIdKey = "Bff__Authentication__ClientId";
        const string secretKey = "Bff__Authentication__ClientSecret";
        var previousClientId = Environment.GetEnvironmentVariable(clientIdKey);
        var previousSecret = Environment.GetEnvironmentVariable(secretKey);
        var environmentSecret = Guid.CreateVersion7().ToString("N");

        try
        {
            Environment.SetEnvironmentVariable(clientIdKey, "environment-section-client");
            Environment.SetEnvironmentVariable(secretKey, environmentSecret);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Bff:Authentication:Authority"] = "https://section.example/realms/events",
                    ["Keycloak:ClientId"] = "fallback-client",
                    ["Keycloak:ClientSecret"] = Guid.CreateVersion7().ToString("N")
                })
                .AddEnvironmentVariables()
                .Build();
            var builder = WebApplication.CreateBuilder(
                new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Services.AddEventBffKeycloakAuthentication(
                configuration, builder.Environment, EventBffHostProfile.PublicWeb);

            await using var provider = builder.Services.BuildServiceProvider();
            var oidc = provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
                .Get(EventBffAuthenticationSchemes.Keycloak);

            await Assert.That(oidc.ClientId).IsEqualTo("environment-section-client");
            await Assert.That(oidc.ClientSecret).IsEqualTo(environmentSecret);
        }
        finally
        {
            Environment.SetEnvironmentVariable(clientIdKey, previousClientId);
            Environment.SetEnvironmentVariable(secretKey, previousSecret);
        }
    }

    [Test]
    public async Task ControlPlaneWithOnlyPublicKeycloakIdentityConfigurationFailsClosed()
    {
        var builder = CreateBuilder(new Dictionary<string, string?>
        {
            ["Keycloak:Authority"] = "https://public.example/realms/events",
            ["Keycloak:ClientId"] = "public-client",
            ["Keycloak:ClientSecret"] = Guid.CreateVersion7().ToString("N")
        });

        Action register = () => builder.Services.AddEventBffKeycloakAuthentication(
            builder.Configuration, builder.Environment, EventBffHostProfile.ControlPlane);

        var exception = await Assert.That(register).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains("Bff:Authentication:Authority");
    }

    [Test]
    public async Task ControlPlaneOwnChildrenOverrideEveryPublicFallbackInRegisteredHandlers()
    {
        var privateSecret = Guid.CreateVersion7().ToString("N");
        var builder = CreateBuilder(new Dictionary<string, string?>
        {
            ["Bff:Authentication:Authority"] = "https://private.example/realms/events",
            ["Bff:Authentication:MetadataAddress"] = "https://private.example/metadata",
            ["Bff:Authentication:ClientId"] = "private-client",
            ["Bff:Authentication:ClientSecret"] = privateSecret,
            ["Bff:Authentication:RequireHttpsMetadata"] = "false",
            ["Bff:Authentication:CallbackPath"] = "/private-signin",
            ["Bff:Authentication:SignedOutCallbackPath"] = "/private-signout",
            ["Bff:Authentication:LoginPath"] = "/private-login",
            ["Bff:Authentication:AccessDeniedPath"] = "/private-denied",
            ["Bff:Authentication:CookieName"] = "private-cookie",
            ["Bff:Authentication:CookieLifetimeMinutes"] = "19",
            ["Bff:Authentication:RequireInstanceAdminClaim"] = "false",
            ["Bff:Authentication:InstanceAdminClaimType"] = "private-admin-type",
            ["Bff:Authentication:InstanceAdminClaimValue"] = "private-admin-value",
            ["Keycloak:Authority"] = "https://public.example/realms/events",
            ["Keycloak:MetadataAddress"] = "https://public.example/metadata",
            ["Keycloak:ClientId"] = "public-client",
            ["Keycloak:ClientSecret"] = Guid.CreateVersion7().ToString("N"),
            ["Keycloak:RequireHttpsMetadata"] = "true",
            ["Bff:Cookie:LoginPath"] = "/public-login",
            ["Bff:Cookie:AccessDeniedPath"] = "/public-denied",
            ["Bff:Cookie:Name"] = "public-cookie",
            ["Bff:Cookie:LifetimeMinutes"] = "91",
            ["Bff:Security:RequireInstanceAdminClaim"] = "true",
            ["Bff:Security:InstanceAdminClaimType"] = "public-admin-type",
            ["Bff:Security:InstanceAdminClaimValue"] = "public-admin-value"
        });
        builder.Services.AddEventBffKeycloakAuthentication(
            builder.Configuration, builder.Environment, EventBffHostProfile.ControlPlane);

        await using var provider = builder.Services.BuildServiceProvider();
        var oidc = provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(EventBffAuthenticationSchemes.Keycloak);
        var cookie = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var bound = provider.GetRequiredService<EventBffKeycloakAuthenticationOptions>();

        await Assert.That(oidc.Authority).IsEqualTo("https://private.example/realms/events");
        await Assert.That(oidc.MetadataAddress).IsEqualTo("https://private.example/metadata");
        await Assert.That(oidc.ClientId).IsEqualTo("private-client");
        await Assert.That(oidc.ClientSecret).IsEqualTo(privateSecret);
        await Assert.That(oidc.CallbackPath.Value).IsEqualTo("/private-signin");
        await Assert.That(oidc.SignedOutCallbackPath.Value).IsEqualTo("/private-signout");
        await Assert.That(cookie.LoginPath.Value).IsEqualTo("/private-login");
        await Assert.That(cookie.AccessDeniedPath.Value).IsEqualTo("/private-denied");
        await Assert.That(cookie.Cookie.Name).IsEqualTo("private-cookie");
        await Assert.That(cookie.ExpireTimeSpan).IsEqualTo(TimeSpan.FromMinutes(19));
        await Assert.That(bound.RequireInstanceAdminClaim).IsFalse();
        await Assert.That(bound.InstanceAdminClaimType).IsEqualTo("private-admin-type");
        await Assert.That(bound.InstanceAdminClaimValue).IsEqualTo("private-admin-value");
    }

    [Test]
    public async Task ControlPlaneUsesSharedCookieAndSecurityFallbacksWhenOwnChildrenAreMissing()
    {
        var builder = CreateBuilder(new Dictionary<string, string?>
        {
            ["Bff:Authentication:Authority"] = "https://private.example/realms/events",
            ["Bff:Authentication:ClientId"] = "private-client",
            ["Bff:Authentication:ClientSecret"] = Guid.CreateVersion7().ToString("N"),
            ["Bff:Cookie:LoginPath"] = "/shared-login",
            ["Bff:Cookie:AccessDeniedPath"] = "/shared-denied",
            ["Bff:Cookie:Name"] = "shared-cookie",
            ["Bff:Cookie:LifetimeMinutes"] = "37",
            ["Bff:Security:RequireInstanceAdminClaim"] = "false",
            ["Bff:Security:InstanceAdminClaimType"] = "shared-admin-type",
            ["Bff:Security:InstanceAdminClaimValue"] = "shared-admin-value"
        });
        builder.Services.AddEventBffKeycloakAuthentication(
            builder.Configuration, builder.Environment, EventBffHostProfile.ControlPlane);

        await using var provider = builder.Services.BuildServiceProvider();
        var cookie = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var bound = provider.GetRequiredService<EventBffKeycloakAuthenticationOptions>();

        await Assert.That(cookie.LoginPath.Value).IsEqualTo("/shared-login");
        await Assert.That(cookie.AccessDeniedPath.Value).IsEqualTo("/shared-denied");
        await Assert.That(cookie.Cookie.Name).IsEqualTo("shared-cookie");
        await Assert.That(cookie.ExpireTimeSpan).IsEqualTo(TimeSpan.FromMinutes(37));
        await Assert.That(bound.RequireInstanceAdminClaim).IsFalse();
        await Assert.That(bound.InstanceAdminClaimType).IsEqualTo("shared-admin-type");
        await Assert.That(bound.InstanceAdminClaimValue).IsEqualTo("shared-admin-value");
    }

    [Test]
    public async Task ControlPlaneRejectsEachPublicIdentityFallbackIndividually()
    {
        var privateSecret = Guid.CreateVersion7().ToString("N");
        var authorityBuilder = CreateBuilder(new Dictionary<string, string?>
        {
            ["Bff:Authentication:MetadataAddress"] = "https://private.example/metadata",
            ["Bff:Authentication:ClientId"] = "private-client",
            ["Bff:Authentication:ClientSecret"] = privateSecret,
            ["Keycloak:Authority"] = "https://public.example/realms/events"
        });
        authorityBuilder.Services.AddEventBffKeycloakAuthentication(
            authorityBuilder.Configuration, authorityBuilder.Environment, EventBffHostProfile.ControlPlane);
        await using var authorityProvider = authorityBuilder.Services.BuildServiceProvider();
        var authorityOidc = authorityProvider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(EventBffAuthenticationSchemes.Keycloak);

        var metadata = EventBffKeycloakAuthenticationOptions.FromConfiguration(
            CreateBuilder(new Dictionary<string, string?>
            {
                ["Bff:Authentication:Authority"] = "https://private.example/realms/events",
                ["Bff:Authentication:ClientId"] = "private-client",
                ["Bff:Authentication:ClientSecret"] = privateSecret,
                ["Keycloak:MetadataAddress"] = "https://public.example/metadata"
            }).Configuration,
            EventBffHostProfile.ControlPlane,
            authorityBuilder.Environment);

        await Assert.That(authorityOidc.Authority).IsNull();
        await Assert.That(metadata.MetadataAddress).IsNull();

        foreach (string missingChild in new[] { "ClientId", "ClientSecret" })
        {
            var values = new Dictionary<string, string?>
            {
                ["Bff:Authentication:Authority"] = "https://private.example/realms/events",
                ["Bff:Authentication:ClientId"] = "private-client",
                ["Bff:Authentication:ClientSecret"] = privateSecret,
                [$"Keycloak:{missingChild}"] = missingChild == "ClientSecret"
                    ? Guid.CreateVersion7().ToString("N")
                    : "public-client"
            };
            values.Remove($"Bff:Authentication:{missingChild}");
            var candidate = CreateBuilder(values);

            Action register = () => candidate.Services.AddEventBffKeycloakAuthentication(
                candidate.Configuration, candidate.Environment, EventBffHostProfile.ControlPlane);

            await Assert.That(register).Throws<InvalidOperationException>();
        }
    }

    private static WebApplicationBuilder CreateBuilder(Dictionary<string, string?> values)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(values);
        return builder;
    }
}
