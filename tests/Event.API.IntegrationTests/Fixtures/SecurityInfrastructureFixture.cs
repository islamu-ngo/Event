using TUnit.Core.Interfaces;

namespace Event.Api.IntegrationTests.Fixtures;

/// <summary>
/// Composite fixture orchestrating Keycloak + Cerbos container lifecycle.
/// Start both containers in parallel, expose their endpoints for downstream
/// <see cref="SecurityWebApplicationFactory"/> and <see cref="CerbosPolicyContractTests"/>.
/// </summary>
public sealed class SecurityInfrastructureFixture : IAsyncInitializer, IAsyncDisposable
{
    private readonly KeycloakContainerFixture _keycloak = new();
    private readonly CerbosContainerFixture _cerbos = new();

    /// <summary>
    /// Token client delegating to the Keycloak container.
    /// </summary>
    public KeycloakTokenClient TokenClient => _keycloak.TokenClient;

    /// <summary>
    /// The OIDC authority URL from the Keycloak container.
    /// </summary>
    public string KeycloakAuthority => _keycloak.Authority;

    public string KeycloakBaseUrl => _keycloak.BaseUrl;

    /// <summary>
    /// The OIDC metadata address from the Keycloak container.
    /// </summary>
    public string KeycloakMetadataAddress => _keycloak.MetadataAddress;

    public KeycloakTokenClient CreateTokenClient(string clientSecret)
        => new(KeycloakBaseUrl, KeycloakContainerFixture.RealmName, KeycloakContainerFixture.TestClientId, clientSecret);

    /// <summary>
    /// The Cerbos gRPC endpoint for SDK clients.
    /// </summary>
    public string CerbosGrpcEndpoint => _cerbos.GrpcEndpoint;

    /// <summary>
    /// The Cerbos HTTP endpoint for REST API and health checks.
    /// </summary>
    public string CerbosHttpEndpoint => _cerbos.HttpEndpoint;

    public async Task InitializeAsync()
    {
        // Start both containers in parallel for faster fixture setup.
        var keycloakTask = _keycloak.InitializeAsync();
        var cerbosTask = _cerbos.InitializeAsync();

        await Task.WhenAll(keycloakTask, cerbosTask);
    }

    public async ValueTask DisposeAsync()
    {
        await _keycloak.DisposeAsync();
        await _cerbos.DisposeAsync();
    }
}
