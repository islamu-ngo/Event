using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using TUnit.Core.Interfaces;

namespace Event.Api.IntegrationTests.Fixtures;

/// <summary>
/// Manages a Keycloak container with the deterministic ISLAMU test realm.
/// Provides the OIDC authority URL and a <see cref="KeycloakTokenClient"/> for acquiring real JWTs.
/// </summary>
public sealed class KeycloakContainerFixture : IAsyncInitializer, IAsyncDisposable
{
    /// <summary>Pinned Keycloak version aligned with docker-compose.yml.</summary>
    private const string KeycloakImage = "quay.io/keycloak/keycloak:26.1.2";

    /// <summary>Realm name matching the test realm export.</summary>
    public const string RealmName = "ISLAMU";

    /// <summary>Client ID with directAccessGrantsEnabled for ROPC token acquisition.</summary>
    public const string TestClientId = "islamu-event-blazor";

    /// <summary>Ephemeral client credential owned by this disposable container.</summary>
    public string ClientSecret { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public string BootstrapAdminPassword { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(2);

    private IContainer _container = null!;
    private readonly Dictionary<string, string> _userPasswords = new(StringComparer.Ordinal);

    /// <summary>
    /// The OIDC authority URL (e.g., <c>http://localhost:{port}/realms/ISLAMU</c>).
    /// Use this as the JWT Bearer authority in SecurityWebApplicationFactory.
    /// </summary>
    public string Authority { get; private set; } = string.Empty;

    public string BaseUrl { get; private set; } = string.Empty;

    /// <summary>
    /// Full OIDC metadata endpoint URL.
    /// </summary>
    public string MetadataAddress { get; private set; } = string.Empty;

    /// <summary>
    /// Token client for acquiring real JWTs from the containerized Keycloak.
    /// </summary>
    public KeycloakTokenClient TokenClient { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var testRealmPath = Path.Combine(AppContext.BaseDirectory, "TestAssets", "ISLAMU-realm.test.json");

        if (!File.Exists(testRealmPath))
        {
            throw new FileNotFoundException(
                $"Test realm export not found at '{testRealmPath}'. " +
                "Ensure ISLAMU-realm.test.json is included as Content with CopyToOutputDirectory=PreserveNewest.");
        }

        JsonObject realm = JsonNode.Parse(await File.ReadAllTextAsync(testRealmPath))!.AsObject();
        JsonNode client = realm["clients"]!.AsArray()
            .Single(candidate => candidate!["clientId"]!.GetValue<string>() == TestClientId)!;
        client["secret"] = ClientSecret;
        foreach (JsonNode? user in realm["users"]!.AsArray())
        {
            string username = user!["username"]!.GetValue<string>();
            string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _userPasswords.Add(username, password);
            user["credentials"] = new JsonArray(new JsonObject
            {
                ["type"] = "password",
                ["value"] = password,
                ["temporary"] = false
            });
        }

        _container = new ContainerBuilder()
            .WithImage(KeycloakImage)
            .WithPortBinding(8080, true)
            .WithResourceMapping(JsonSerializer.SerializeToUtf8Bytes(realm),
                "/opt/keycloak/data/import/ISLAMU-realm.test.json")
            .WithCommand("start-dev", "--import-realm", "--http-port=8080")
            .WithEnvironment("KC_HEALTH_ENABLED", "true")
            .WithEnvironment("KC_HTTP_ENABLED", "true")
            .WithEnvironment("KEYCLOAK_ADMIN", "admin")
            .WithEnvironment("KEYCLOAK_ADMIN_PASSWORD", BootstrapAdminPassword)
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

        BaseUrl = $"http://{host}:{port}";
        Authority = $"{BaseUrl}/realms/{RealmName}";
        MetadataAddress = $"{Authority}/.well-known/openid-configuration";

        TokenClient = CreateTokenClient(ClientSecret);
    }

    public KeycloakTokenClient CreateTokenClient(string clientSecret) => new(
        keycloakBaseUrl: BaseUrl,
        realm: RealmName,
        clientId: TestClientId,
        clientSecret: clientSecret,
        userPasswords: _userPasswords);

    public async ValueTask DisposeAsync()
    {
        TokenClient?.Dispose();
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
