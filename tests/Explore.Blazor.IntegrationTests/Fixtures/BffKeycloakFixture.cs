using System.Security.Cryptography;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using TUnit.Core.Interfaces;

namespace Explore.Blazor.IntegrationTests.Fixtures;

/// <summary>
/// Keycloak container fixture for Blazor BFF security tests.
/// Provides the OIDC authority URL for the BFF's DynamicAuthSchemeManager.
/// </summary>
public sealed class BffKeycloakFixture : IAsyncInitializer, IAsyncDisposable
{
    private const string KeycloakImage = "quay.io/keycloak/keycloak:26.1.2";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(2);

    public const string RealmName = "ISLAMU";
    public const string TestClientId = "islamu-event-blazor";

    private IContainer _container = null!;
    private readonly string _administratorPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public string Authority { get; private set; } = string.Empty;
    public string MetadataAddress { get; private set; } = string.Empty;
    public string ClientSecret { get; private set; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public string TestUserPassword { get; } = $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";

    public async Task InitializeAsync()
    {
        var testRealmPath = Path.Combine(
            AppContext.BaseDirectory, "TestAssets", "ISLAMU-realm.test.json");

        if (!File.Exists(testRealmPath))
        {
            throw new FileNotFoundException(
                $"Test realm export not found at '{testRealmPath}'. " +
                "Ensure ISLAMU-realm.test.json is included as Content.");
        }

        var realm = JsonNode.Parse(await File.ReadAllTextAsync(testRealmPath))?.AsObject()
            ?? throw new InvalidDataException("The test realm must be a JSON object.");
        var client = realm["clients"]!.AsArray().Single(node =>
            node?["clientId"]?.GetValue<string>() == TestClientId)!;
        client["secret"] = ClientSecret;
        var user = realm["users"]!.AsArray().Single(node =>
            node?["username"]?.GetValue<string>() == "test-user")!;
        user["credentials"] = new JsonArray(new JsonObject
        {
            ["type"] = "password",
            ["value"] = TestUserPassword,
            ["temporary"] = false
        });

        _container = new ContainerBuilder()
            .WithImage(KeycloakImage)
            .WithPortBinding(8080, true)
            .WithResourceMapping(
                JsonSerializer.SerializeToUtf8Bytes(realm),
                "/opt/keycloak/data/import/ISLAMU-realm.test.json")
            .WithCommand("start-dev", "--import-realm", "--http-port=8080")
            .WithEnvironment("KC_HEALTH_ENABLED", "true")
            .WithEnvironment("KC_HTTP_ENABLED", "true")
            .WithEnvironment("KEYCLOAK_ADMIN", "test-admin")
            .WithEnvironment(
                "KEYCLOAK_ADMIN_PASSWORD",
                _administratorPassword)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(request =>
                    request
                        .ForPath($"/realms/{RealmName}/.well-known/openid-configuration")
                        .ForPort(8080)
                        .ForStatusCode(System.Net.HttpStatusCode.OK),
                    wait => wait.WithTimeout(StartupTimeout)))
            .Build();

        using var startupCts = new CancellationTokenSource(StartupTimeout);
        await _container.StartAsync(startupCts.Token);

        var host = _container.Hostname;
        var port = _container.GetMappedPublicPort(8080);

        Authority = $"http://{host}:{port}/realms/{RealmName}";
        MetadataAddress = $"{Authority}/.well-known/openid-configuration";
    }

    /// <summary>
    /// Replaces only the fixture-owned client's credential on its disposable provider.
    /// The administrator token is confined to this operation and never given to the BFF.
    /// </summary>
    public async Task RotateClientSecretAsync(CancellationToken cancellationToken)
    {
        string providerRoot = Authority[..Authority.IndexOf("/realms/", StringComparison.Ordinal)];
        using var client = await CreateAdministratorClientAsync(cancellationToken);
        using var lookup = await client.GetAsync(
            $"{providerRoot}/admin/realms/{RealmName}/clients?clientId={Uri.EscapeDataString(TestClientId)}",
            cancellationToken);
        lookup.EnsureSuccessStatusCode();
        var clients = JsonNode.Parse(await lookup.Content.ReadAsStringAsync(cancellationToken))!.AsArray();
        JsonObject representation = clients.Single()!.AsObject();
        string clientId = representation["id"]!.GetValue<string>();
        string replacement = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        representation["secret"] = replacement;
        using var content = JsonContent.Create(representation);
        using var changed = await client.PutAsync(
            $"{providerRoot}/admin/realms/{RealmName}/clients/{Uri.EscapeDataString(clientId)}",
            content, cancellationToken);
        changed.EnsureSuccessStatusCode();
        ClientSecret = replacement;
    }

    public async Task UseShortLivedAccessTokensAsync(CancellationToken cancellationToken)
    {
        string providerRoot = Authority[..Authority.IndexOf("/realms/", StringComparison.Ordinal)];
        using var client = await CreateAdministratorClientAsync(cancellationToken);
        using var content = JsonContent.Create(new { accessTokenLifespan = 30 });
        using var changed = await client.PutAsync(
            $"{providerRoot}/admin/realms/{RealmName}", content, cancellationToken);
        changed.EnsureSuccessStatusCode();
    }

    public async Task AddCallbackAsync(Uri callback, CancellationToken cancellationToken)
    {
        string providerRoot = Authority[..Authority.IndexOf("/realms/", StringComparison.Ordinal)];
        using var client = await CreateAdministratorClientAsync(cancellationToken);
        string collection = $"{providerRoot}/admin/realms/{RealmName}/clients";
        using var lookup = await client.GetAsync(
            $"{collection}?clientId={Uri.EscapeDataString(TestClientId)}", cancellationToken);
        lookup.EnsureSuccessStatusCode();
        var clients = JsonNode.Parse(await lookup.Content.ReadAsStringAsync(cancellationToken))!.AsArray();
        var representation = clients.Single()!.AsObject();
        representation["redirectUris"]!.AsArray().Add(callback.AbsoluteUri);
        using var content = JsonContent.Create(representation);
        using var changed = await client.PutAsync(
            $"{collection}/{Uri.EscapeDataString(representation["id"]!.GetValue<string>())}",
            content, cancellationToken);
        changed.EnsureSuccessStatusCode();
    }

    private async Task<HttpClient> CreateAdministratorClientAsync(CancellationToken cancellationToken)
    {
        string providerRoot = Authority[..Authority.IndexOf("/realms/", StringComparison.Ordinal)];
        var client = new HttpClient();
        try
        {
            using var credentials = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "admin-cli",
                ["username"] = "test-admin",
                ["password"] = _administratorPassword
            });
            using var tokenResponse = await client.PostAsync(
                $"{providerRoot}/realms/master/protocol/openid-connect/token", credentials, cancellationToken);
            tokenResponse.EnsureSuccessStatusCode();
            using var token = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync(cancellationToken));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", token.RootElement.GetProperty("access_token").GetString());
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

/// <summary>
/// Test category constants for BFF security tests.
/// </summary>
public static class BffTestCategories
{
    public const string Security = "Security";
    public const string Email = "Email";
    public const string Runtime = "Runtime";
    public const string Slow = "Slow";
    public const string Manual = "Manual";
}
