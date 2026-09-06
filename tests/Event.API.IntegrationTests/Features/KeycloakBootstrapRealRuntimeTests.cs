// ABOUTME: Real-runtime tests for setup-time Keycloak bootstrap against a disposable Keycloak container.
// ABOUTME: Verifies the setup endpoint, Infrastructure adapter, and Keycloak token endpoint agree on rotated secrets.

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Onboarding;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[Category(TestCategories.Security)]
[ClassDataSource<KeycloakOnlyFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("SecurityInfra")]
public sealed class KeycloakBootstrapRealRuntimeTests
{
    private const string BaseUrl = "/api/instanceonboarding";
    private readonly string _setupSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private readonly KeycloakOnlyFixture _keycloak;

    public KeycloakBootstrapRealRuntimeTests(KeycloakOnlyFixture keycloak)
    {
        _keycloak = keycloak;
    }

    [Test]
    public async Task BootstrapKeycloakRealm_WithDisposableKeycloak_ShouldRotateSecretAndPersistRuntimeConfig()
    {
        await using var factory = new RealKeycloakBootstrapFactory(
            keycloakBaseUrl: _keycloak.KeycloakBaseUrl,
            setupSecret: _setupSecret);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        string rotatedClientSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var payload = CreateBootstrapRequest(rotatedClientSecret);

        try
        {
            var response = await SendBootstrapRequestAsync(client, payload);

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var commandResponse = await response.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>();
            await Assert.That(commandResponse).IsNotNull();
            await Assert.That(commandResponse!.IsSuccess).IsTrue();
            await Assert.That(commandResponse.Message.Contains(payload.BootstrapAdminPassword, StringComparison.Ordinal)).IsFalse();
            await Assert.That(commandResponse.Message.Contains(payload.BlazorClientSecret, StringComparison.Ordinal)).IsFalse();

            using KeycloakTokenClient rotatedTokenClient = _keycloak.CreateTokenClient(rotatedClientSecret);
            var token = await rotatedTokenClient.GetUserTokenAsync(CancellationToken.None);
            await Assert.That(token).IsNotNull();
            await Assert.That(token).IsNotEmpty();

            var offlineAccessToken = await rotatedTokenClient.GetUserTokenWithOfflineAccessAsync(CancellationToken.None);
            await Assert.That(offlineAccessToken).IsNotNull();
            await Assert.That(offlineAccessToken).IsNotEmpty();

            using var internalConfigRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"{BaseUrl}/auth-provider-configuration/internal");
            internalConfigRequest.Headers.Add("X-Setup-Secret", _setupSecret);
            var internalConfigResponse = await client.SendAsync(internalConfigRequest);

            await Assert.That(internalConfigResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var config = await internalConfigResponse.Content.ReadFromJsonAsync<AuthProviderConfigurationDto>();
            await Assert.That(config).IsNotNull();
            await Assert.That(config!.PrimaryProviderId)
                .IsEqualTo((int)AuthenticationProviderKind.Keycloak);
            await Assert.That(config.KeycloakAuthority).IsEqualTo($"{_keycloak.KeycloakBaseUrl}/realms/{KeycloakContainerFixture.RealmName}");
            await Assert.That(config.KeycloakClientId).IsEqualTo(KeycloakContainerFixture.TestClientId);
            await Assert.That(string.Equals(config.KeycloakClientSecret, rotatedClientSecret, StringComparison.Ordinal)).IsTrue();
            await Assert.That(config.KeycloakClientSecret!.Contains(payload.BootstrapAdminPassword, StringComparison.Ordinal)).IsFalse();
        }
        finally
        {
            await SendBootstrapRequestAsync(client, CreateBootstrapRequest(_keycloak.ClientSecret));
        }
    }

    private async Task<HttpResponseMessage> SendBootstrapRequestAsync(HttpClient client, KeycloakBootstrapRequestDto payload)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{BaseUrl}/auth-provider-configuration/keycloak-bootstrap")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("X-Setup-Secret", _setupSecret);
        return await client.SendAsync(request);
    }

    private KeycloakBootstrapRequestDto CreateBootstrapRequest(string clientSecret)
    {
        return new KeycloakBootstrapRequestDto
        {
            KeycloakBaseUrl = _keycloak.KeycloakBaseUrl,
            Realm = KeycloakContainerFixture.RealmName,
            BlazorClientId = KeycloakContainerFixture.TestClientId,
            BlazorClientSecret = clientSecret,
            ApiClientId = null,
            ApiClientSecret = null,
            Mode = KeycloakBootstrapMode.PatchExistingRealm,
            BootstrapAdminUsername = "admin",
            BootstrapAdminPassword = _keycloak.BootstrapAdminPassword
        };
    }

    private sealed class RealKeycloakBootstrapFactory : AuthenticatedWebApplicationFactory
    {
        private readonly string _keycloakBaseUrl;
        private readonly string _setupSecret;
        private readonly string _databasePath = Path.Combine(
            Path.GetTempPath(), $"keycloak-bootstrap-{Guid.CreateVersion7():N}.db");
        private string? _connectionString;

        public RealKeycloakBootstrapFactory(string keycloakBaseUrl, string setupSecret)
        {
            _keycloakBaseUrl = keycloakBaseUrl;
            _setupSecret = setupSecret;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SETUP_SECRET"] = _setupSecret,
                    ["SETUP_SECRET_FILE"] = _databasePath + ".setup",
                    ["Database:Provider"] = "Sqlite",
                    ["Database:Database"] = _databasePath,
                    ["Database:Runtime:Database"] = _databasePath,
                    ["KeycloakBootstrap:AllowLocalUrls"] = "true",
                    ["Keycloak:Authority"] = $"{_keycloakBaseUrl}/realms/{KeycloakContainerFixture.RealmName}",
                    ["Keycloak:MetadataAddress"] = $"{_keycloakBaseUrl}/realms/{KeycloakContainerFixture.RealmName}/.well-known/openid-configuration",
                    ["Keycloak:Realm"] = KeycloakContainerFixture.RealmName,
                    ["Keycloak:RequireHttpsMetadata"] = "false"
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveExploreDbContextRegistrations();
                services.AddDbContextFactory<ExploreDbContext>(ConfigureDatabase);
                services.AddScoped(provider =>
                {
                    var context = provider.GetRequiredService<IDbContextFactory<ExploreDbContext>>()
                        .CreateDbContext();
                    context.TenantContext = provider.GetService<ITenantContext>();
                    context.CurrentUserService = provider.GetService<ICurrentUserService>();
                    context.ClearTenantFilterBypass();
                    return context;
                });
            });
        }

        public async Task InitializeDatabaseAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var options = new DbContextOptionsBuilder<ExploreDbContext>();
            ConfigureDatabase(options);
            await using var database = new ExploreDbContext(options.Options);
            await database.Database.EnsureCreatedAsync(timeout.Token);
            await SqliteDatabaseInitializer.InitializeAsync(database, timeout.Token);
        }

        private void ConfigureDatabase(DbContextOptionsBuilder options)
        {
            PrimaryDatabaseConnectionResult database = PrimaryDatabaseProviderComposition.ConfigureApplication(
                optionsBuilder: options,
                options: new PrimaryDatabaseConnectionOptions
                {
                    Role = PrimaryDatabaseRole.Runtime,
                    Provider = PrimaryDatabaseProvider.Sqlite,
                    Database = _databasePath
                });
            _connectionString = database.ConnectionString;
            options.UseSnakeCaseNamingConvention();
        }

        public override async ValueTask DisposeAsync()
        {
            try
            {
                await base.DisposeAsync();
            }
            finally
            {
                if (_connectionString is not null)
                {
                    using var connection = new SqliteConnection(_connectionString);
                    SqliteConnection.ClearPool(connection);
                }

                File.Delete(_databasePath);
                File.Delete(_databasePath + "-wal");
                File.Delete(_databasePath + "-shm");
                File.Delete(_databasePath + ".setup");
            }
        }
    }
}
