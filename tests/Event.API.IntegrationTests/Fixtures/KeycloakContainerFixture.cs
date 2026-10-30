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
    public const string NoAudienceClientId = "token-context-no-audience";
    public const string ServiceClientId = "token-context-service";

    /// <summary>Ephemeral client credential owned by this disposable container.</summary>
    public string ClientSecret { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public string BootstrapAdminPassword { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(2);

    private IContainer? _container;
    private SecurityInfrastructureFixture.ContainerCreationObservation? _creationObservation;
    private readonly object _disposalSync = new();
    private Task? _disposal;
    private readonly Dictionary<string, string> _userPasswords = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _clientSecrets = new(StringComparer.Ordinal);

    public KeycloakContainerFixture() { }

    internal KeycloakContainerFixture(IContainer container,
        SecurityInfrastructureFixture.ContainerCreationObservation observation)
    {
        _container = container;
        _creationObservation = observation;
    }

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
        _clientSecrets.Add(TestClientId, ClientSecret);
        foreach (string clientId in new[] { NoAudienceClientId, ServiceClientId })
        {
            JsonNode additional = realm["clients"]!.AsArray()
                .Single(candidate => candidate!["clientId"]!.GetValue<string>() == clientId)!;
            string secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _clientSecrets.Add(clientId, secret);
            additional["secret"] = secret;
        }
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

        var ownership = await SecurityInfrastructureFixture.WithAssuranceOwnershipAsync(new ContainerBuilder(KeycloakImage), "keycloak");
        var container = ownership.Builder
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
        _container = container;
        using var startupCts = new CancellationTokenSource(StartupTimeout);
        try
        {
            _creationObservation = await SecurityInfrastructureFixture.BeginCreationObservationAsync(
                ownership.Ownership, "keycloak", container, startupCts.Token);
            await SecurityInfrastructureFixture.StartObservedContainerAsync(container, _creationObservation, startupCts.Token);
            var host = container.Hostname;
            var port = container.GetMappedPublicPort(8080);

            BaseUrl = $"http://{host}:{port}";
            Authority = $"{BaseUrl}/realms/{RealmName}";
            MetadataAddress = $"{Authority}/.well-known/openid-configuration";

            TokenClient = CreateTokenClient(ClientSecret);
        }
        catch (Exception primary)
        {
            try { await DisposeAsync(); }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
    }

    public KeycloakTokenClient CreateTokenClient(string clientSecret) => new(
        keycloakBaseUrl: BaseUrl,
        realm: RealmName,
        clientId: TestClientId,
        clientSecret: clientSecret,
        userPasswords: _userPasswords);

    public KeycloakTokenClient CreateContextTokenClient(string clientId) => new(
        keycloakBaseUrl: BaseUrl,
        realm: RealmName,
        clientId: clientId,
        clientSecret: _clientSecrets[clientId],
        userPasswords: _userPasswords);

    public ValueTask DisposeAsync()
    {
        lock (_disposalSync) return new ValueTask(_disposal ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        var client = TokenClient;
        TokenClient = null!;
        Exception? primary = null;
        try { client?.Dispose(); }
        catch (Exception exception) { primary = exception; }
        var container = _container;
        var observation = _creationObservation;
        _container = null;
        _creationObservation = null;
        try
        {
            await SecurityInfrastructureFixture.DisposeObservedContainerAsync(container, observation);
        }
        catch (Exception cleanup)
        {
            if (primary is not null) throw new AggregateException(primary, cleanup);
            throw;
        }
        if (primary is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
    }
}
