// ABOUTME: Keycloak container fixture for BFF security integration tests.
// ABOUTME: Imports the ISLAMU test realm with per-instance credentials injected in memory for OIDC tests.

using System.Security.Cryptography;
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

    public string Authority { get; private set; } = string.Empty;
    public string MetadataAddress { get; private set; } = string.Empty;
    public string ClientSecret { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
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
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32)))
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
