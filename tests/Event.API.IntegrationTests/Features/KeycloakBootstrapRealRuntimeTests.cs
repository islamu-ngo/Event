// ABOUTME: Real-runtime tests for setup-time Keycloak bootstrap against a disposable Keycloak container.
// ABOUTME: Verifies the setup endpoint, Infrastructure adapter, and Keycloak token endpoint agree on rotated secrets.

using System.Net;
using System.Net.Http.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Onboarding;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

namespace Event.Api.IntegrationTests.Features;

[Category(TestCategories.Security)]
[ClassDataSource<KeycloakOnlyFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("SecurityInfra")]
public sealed class KeycloakBootstrapRealRuntimeTests
{
    private const string BaseUrl = "/api/instanceonboarding";
    private static string SetupSecret => OnboardingWebApplicationFactory.SetupSecret;
    private static string RotatedBlazorSecret =>
        OnboardingWebApplicationFactory.RequireSecret("KEYCLOAK_BLAZOR_CLIENT_SECRET");

    private readonly KeycloakOnlyFixture _keycloak;

    public KeycloakBootstrapRealRuntimeTests(KeycloakOnlyFixture keycloak)
    {
        _keycloak = keycloak;
    }

    [Test]
    public async Task BootstrapKeycloakRealm_WithDisposableKeycloak_ShouldRotateSecretAndPersistRuntimeConfig()
    {
        using var factory = new RealKeycloakBootstrapFactory(_keycloak.KeycloakBaseUrl);
        using var client = factory.CreateClient();
        await Assert.That(RotatedBlazorSecret).IsNotEqualTo(KeycloakContainerFixture.TestClientSecret);
        var payload = CreateBootstrapRequest(_keycloak.KeycloakBaseUrl, RotatedBlazorSecret);

        try
        {
            var response = await SendBootstrapRequestAsync(client, payload);

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var commandResponse = await response.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>();
            await Assert.That(commandResponse).IsNotNull();
            await Assert.That(commandResponse!.IsSuccess).IsTrue();
            await Assert.That(commandResponse.Message).DoesNotContain(payload.BootstrapAdminPassword);
            await Assert.That(commandResponse.Message).DoesNotContain(payload.BlazorClientSecret);

            var token = await _keycloak.CreateTokenClient(RotatedBlazorSecret)
                .GetUserTokenAsync(CancellationToken.None);
            await Assert.That(token).IsNotNull();
            await Assert.That(token).IsNotEmpty();

            var offlineAccessToken = await _keycloak.CreateTokenClient(RotatedBlazorSecret)
                .GetUserTokenWithOfflineAccessAsync(CancellationToken.None);
            await Assert.That(offlineAccessToken).IsNotNull();
            await Assert.That(offlineAccessToken).IsNotEmpty();

            using var internalConfigRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"{BaseUrl}/auth-provider-configuration/internal");
            internalConfigRequest.Headers.Add("X-Setup-Secret", SetupSecret);
            var internalConfigResponse = await client.SendAsync(internalConfigRequest);

            await Assert.That(internalConfigResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var config = await internalConfigResponse.Content.ReadFromJsonAsync<AuthProviderConfigurationDto>();
            await Assert.That(config).IsNotNull();
            await Assert.That(config!.PrimaryProviderId)
                .IsEqualTo((int)AuthenticationProviderKind.Keycloak);
            await Assert.That(config.KeycloakAuthority).IsEqualTo($"{_keycloak.KeycloakBaseUrl}/realms/{KeycloakContainerFixture.RealmName}");
            await Assert.That(config.KeycloakClientId).IsEqualTo(KeycloakContainerFixture.TestClientId);
            await Assert.That(config.KeycloakClientSecret).IsEqualTo(RotatedBlazorSecret);
            await Assert.That(config.KeycloakClientSecret).DoesNotContain(payload.BootstrapAdminPassword);
        }
        finally
        {
            using var restoreResponse = await SendBootstrapRequestAsync(
                client, CreateBootstrapRequest(_keycloak.KeycloakBaseUrl, KeycloakContainerFixture.TestClientSecret));
            await Assert.That(restoreResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
    }

    private static async Task<HttpResponseMessage> SendBootstrapRequestAsync(HttpClient client, KeycloakBootstrapRequestDto payload)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{BaseUrl}/auth-provider-configuration/keycloak-bootstrap")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("X-Setup-Secret", SetupSecret);
        return await client.SendAsync(request);
    }

    private static KeycloakBootstrapRequestDto CreateBootstrapRequest(string keycloakBaseUrl, string blazorSecret)
    {
        return new KeycloakBootstrapRequestDto
        {
            KeycloakBaseUrl = keycloakBaseUrl,
            Realm = KeycloakContainerFixture.RealmName,
            BlazorClientId = KeycloakContainerFixture.TestClientId,
            BlazorClientSecret = blazorSecret,
            ApiClientId = null,
            ApiClientSecret = null,
            Mode = KeycloakBootstrapMode.PatchExistingRealm,
            BootstrapAdminUsername = "admin",
            BootstrapAdminPassword = "admin"
        };
    }

    private sealed class RealKeycloakBootstrapFactory : OnboardingWebApplicationFactory
    {
        private readonly string _keycloakBaseUrl;

        public RealKeycloakBootstrapFactory(string keycloakBaseUrl)
        {
            _keycloakBaseUrl = keycloakBaseUrl;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SETUP_SECRET"] = SetupSecret,
                    ["KeycloakBootstrap:AllowLocalUrls"] = "true",
                    ["Keycloak:Authority"] = $"{_keycloakBaseUrl}/realms/{KeycloakContainerFixture.RealmName}",
                    ["Keycloak:MetadataAddress"] = $"{_keycloakBaseUrl}/realms/{KeycloakContainerFixture.RealmName}/.well-known/openid-configuration",
                    ["Keycloak:Realm"] = KeycloakContainerFixture.RealmName,
                    ["Keycloak:RequireHttpsMetadata"] = "false"
                });
            });
        }
    }
}
