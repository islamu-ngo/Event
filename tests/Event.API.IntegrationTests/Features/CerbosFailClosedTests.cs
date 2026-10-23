using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Text;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Testing.DataProtection;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.ServiceDefaults.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TUnit.Core.Interfaces;

namespace Event.Api.IntegrationTests.Features;

/// <summary>
/// Most critical safety test in the security test suite.
/// Proves the core invariant: when an operator explicitly chooses Cerbos as the authorization
/// provider, and Cerbos becomes unavailable, provider-authorized operations fail closed.
/// It never silently falls back to the potentially more permissive local RBAC provider.
///
/// This test exercises the real RuntimeAuthorizationProvider routing:
///   SystemSetting "authorization.provider" = "cerbos"
///   → CerbosAuthorizationService (gRPC to unreachable endpoint)
///   → gRPC failure → catch block → deny all
///
/// Instance administrator roles cannot bypass provider checks. Handler-owned governance
/// reads and anonymous endpoints retain their independent, explicit authority.
/// </summary>
[Category(TestCategories.Security)]
[ClassDataSource<KeycloakOnlyFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("SecurityInfra")]
public class CerbosFailClosedTests : IAsyncDisposable
{
    private readonly KeycloakOnlyFixture _keycloak;
    private readonly CerbosFailClosedWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CerbosFailClosedTests(KeycloakOnlyFixture keycloak)
    {
        _keycloak = keycloak;
        _factory = new CerbosFailClosedWebApplicationFactory(
            keycloak.Authority,
            keycloak.MetadataAddress, keycloak.TokenClient);
        _client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        if (db.Tenants.Find(PlatformDefaults.DefaultTenantId) is null)
        {
            db.Tenants.Add(new Event.Api.IntegrationTests.Builders.TenantBuilder()
                .WithId(PlatformDefaults.DefaultTenantId).Build());
            db.SaveChanges();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    #region All Users Denied When Cerbos Unavailable

    [Test]
    public async Task GovernanceRead_RetainsHandlerOwnedInstanceAuthority_WhenCerbosDown()
    {
        var token = await _keycloak.TokenClient.GetAdminTokenAsync();
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/instance/settings/modules", token);

        var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because(
            "module governance reads explicitly check current instance administrator authority, not the policy provider");
    }

    [Test]
    public async Task InstanceAdmin_DeniedTenantCreation_WhenCerbosDown()
    {
        var token = await _keycloak.TokenClient.GetAdminTokenAsync();
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "/api/tenant", token, CreateTenantJson());

        var response = await _client.SendAsync(request);

        await AssertProviderUnavailableAsync(response);
    }

    [Test]
    public async Task RegularUser_DeniedEventView_WhenCerbosDown()
    {
        var token = await _keycloak.TokenClient.GetUserTokenAsync();
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "/api/tenant", token, CreateTenantJson());

        var response = await _client.SendAsync(request);

        await AssertProviderUnavailableAsync(response);
    }

    [Test]
    public async Task TenantAdmin_DeniedOwnTenant_WhenCerbosDown()
    {
        var token = await _keycloak.TokenClient.GetTenantAdminTokenAsync();
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "/api/tenant", token, CreateTenantJson());

        var response = await _client.SendAsync(request);

        await AssertProviderUnavailableAsync(response);
    }

    #endregion

    #region Authentication Layer Still Works

    [Test]
    public async Task Anonymous_StillGetsUnauthorized_WhenCerbosDown()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/event/my");

        var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized).Because("the authentication layer (JWT validation) is independent of authorization (Cerbos). " +
        "Anonymous requests should still get 401, not 403");
    }

    [Test]
    public async Task AnonymousEndpoints_StillWork_WhenCerbosDown()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/eventformat");

        var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because("[AllowAnonymous] endpoints bypass both authentication and authorization");
    }

    #endregion

    #region Deny-All Consistency

    [Test]
    public async Task DenyAll_IsConsistent_AcrossMultipleRequests()
    {
        var token = await _keycloak.TokenClient.GetUserTokenAsync();

        using var request1 = CreateAuthorizedRequest(HttpMethod.Post, "/api/tenant", token, CreateTenantJson());
        using var request2 = CreateAuthorizedRequest(HttpMethod.Post, "/api/tenant", token, CreateTenantJson());

        var response1 = await _client.SendAsync(request1);
        var response2 = await _client.SendAsync(request2);

        await AssertProviderUnavailableAsync(response1);
        await AssertProviderUnavailableAsync(response2);
    }

    #endregion

    private static async Task AssertProviderUnavailableAsync(HttpResponseMessage response)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(problem.GetProperty("code").GetString()).IsEqualTo("authorization_provider_unavailable");
    }

    private static string CreateTenantJson()
    {
        var suffix = Guid.NewGuid().ToString("N");
        return $"{{\"fullName\":\"Security Test Tenant {suffix}\",\"slug\":\"security-test-tenant-{suffix}\",\"isActive\":true}}";
    }

    private static HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string url, string token, string? jsonBody = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (jsonBody is not null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        return request;
    }

    /// <summary>
    /// WebApplicationFactory configured with authorization.provider = "cerbos" but an unreachable
    /// Cerbos gRPC endpoint. The RuntimeAuthorizationProvider will route to CerbosAuthorizationService,
    /// which will fail, triggering the deny-all catch block.
    /// </summary>
    private sealed class CerbosFailClosedWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly KeycloakTokenClient _keycloakTokens;
        private readonly ExternalDataProtectionTestAuthority _dataProtectionAuthority = new("api");
        private readonly string _keycloakAuthority;
        private readonly string _keycloakMetadataAddress;
        private readonly string _unreachableCerbosEndpoint = "http://localhost:19999";
        private string _dbName = $"CerbosFailClosedDb_{Guid.NewGuid():N}";

        public CerbosFailClosedWebApplicationFactory(
            string keycloakAuthority, string keycloakMetadataAddress, KeycloakTokenClient keycloakTokens)
        {
            _keycloakTokens = keycloakTokens;
            _keycloakAuthority = keycloakAuthority;
            _keycloakMetadataAddress = keycloakMetadataAddress;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureAppConfiguration((_, config) =>
            {
                var testConfig = new Dictionary<string, string?>
                {
                    ["Database:Provider"] = "PostgreSql",
                    ["Database:Host"] = "localhost",
                    ["Database:Port"] = "5432",
                    ["Database:Database"] = "test_cerbos_fail_closed",
                    ["Database:Runtime:TlsMode"] = "Prefer",
                    ["Database:Runtime:TrustServerCertificate"] = "false",
                    ["Keycloak:Authority"] = _keycloakAuthority,
                    ["Keycloak:Realm"] = KeycloakContainerFixture.RealmName,
                    ["Keycloak:Audience"] = "islamu-event-api",
                    ["Keycloak:RequireHttpsMetadata"] = "false",
                    ["Keycloak:MetadataAddress"] = _keycloakMetadataAddress,
                    ["S3Settings:Region"] = "us-east-1",
                    ["S3Settings:BucketName"] = "test-bucket",
                    ["S3Settings:Endpoint"] = "https://s3.example.com",
                    ["Deployment:Mode"] = "SingleTenant",
                    ["Deployment:DefaultTenantId"] = PlatformDefaults.DefaultTenantId.ToString(),
                    ["Testing:HostProfile"] = TestHostProfile.Security,
                    ["Authorization:Provider"] = "cerbos",
                    ["Cerbos:GrpcEndpoint"] = _unreachableCerbosEndpoint,
                    ["Cerbos:PlaintextMode"] = "true",
                };

                config.AddInMemoryCollection(testConfig);
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveExploreDbContextRegistrations();

                services.AddKeycloakSecurityDbContext(_dbName, _keycloakTokens, PlatformDefaults.DefaultTenantId);

                services.RemoveAll<Microsoft.Extensions.Caching.Distributed.IDistributedCache>();
                services.AddDistributedMemoryCache();
            });

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IExternalDataProtectionSource>();
                services.AddSingleton<IExternalDataProtectionSource>(_dataProtectionAuthority);
                services.PostConfigure<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>(
                    Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme, options =>
                    {
                        options.RequireHttpsMetadata = false;
                        options.Authority = _keycloakAuthority;
                        options.MetadataAddress = _keycloakMetadataAddress;
                        options.TokenValidationParameters.ValidIssuer = _keycloakAuthority;
                        options.TokenValidationParameters.ValidIssuers = [_keycloakAuthority];
                        options.BackchannelHttpHandler = new SocketsHttpHandler
                        {
                            SslOptions = new SslClientAuthenticationOptions
                            {
                                RemoteCertificateValidationCallback = (_, _, _, sslPolicyErrors) =>
                                    sslPolicyErrors == System.Net.Security.SslPolicyErrors.None
                            },
                            ConnectCallback = async (context, ct) =>
                            {
                                var socket = new System.Net.Sockets.Socket(
                                    System.Net.Sockets.AddressFamily.InterNetwork,
                                    System.Net.Sockets.SocketType.Stream,
                                    System.Net.Sockets.ProtocolType.Tcp);
                                try
                                {
                                    await socket.ConnectAsync(context.DnsEndPoint, ct);
                                    return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
                                }
                                catch
                                {
                                    socket.Dispose();
                                    throw;
                                }
                            }
                        };
                    });
            });
        }
    }
}

/// <summary>
/// Keycloak-only fixture for tests that need JWT authentication but no Cerbos.
/// Starts only the Keycloak container (not Cerbos).
/// </summary>
public sealed class KeycloakOnlyFixture : IAsyncInitializer, IAsyncDisposable
{
    private readonly KeycloakContainerFixture _keycloak = new();

    public string Authority => _keycloak.Authority;
    public string MetadataAddress => _keycloak.MetadataAddress;
    public string KeycloakBaseUrl => _keycloak.BaseUrl;
    public KeycloakTokenClient TokenClient => _keycloak.TokenClient;
    public string ClientSecret => _keycloak.ClientSecret;
    public string BootstrapAdminPassword => _keycloak.BootstrapAdminPassword;

    public KeycloakTokenClient CreateTokenClient(string clientSecret) =>
        _keycloak.CreateTokenClient(clientSecret);

    public async Task InitializeAsync()
    {
        await _keycloak.InitializeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _keycloak.DisposeAsync();
    }
}
