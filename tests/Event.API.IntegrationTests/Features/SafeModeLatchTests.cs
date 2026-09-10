using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

/// <summary>
/// Tests the BYO Cerbos safe-mode latch mechanism.
///
/// <para>When a tenant has a BYO (Bring Your Own) Cerbos endpoint configured with
/// <c>failure_mode=closed</c> and that endpoint becomes unreachable, the system
/// activates a one-way safe-mode latch on <see cref="Explore.Infrastructure.Services.FallbackAuthorizationService"/>.
/// </para>
///
/// <para>Safe-mode guarantees:</para>
/// <list type="bullet">
/// <item>Only instance admin emergency access is allowed</item>
/// <item>All other users (tenant admin, regular user, org admin) are denied</item>
/// <item>The latch is one-way — safe-mode persists until the instance is restarted</item>
/// <item>Subsequent requests continue to be denied even if the BYO endpoint comes back</item>
/// </list>
///
/// <para>This prevents an attacker from bypassing stricter tenant policies by
/// temporarily disabling the BYO Cerbos endpoint.</para>
/// </summary>
[Category(TestCategories.Security)]
[ClassDataSource<KeycloakOnlyFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("SecurityInfra")]
public class SafeModeLatchTests : IAsyncDisposable
{
    private readonly KeycloakOnlyFixture _keycloak;

    private readonly WebApplicationFactory<Program> _instanceAdminFactory;
    private readonly HttpClient _instanceAdminClient;

    private readonly WebApplicationFactory<Program> _regularUserFactory;
    private readonly HttpClient _regularUserClient;

    private readonly WebApplicationFactory<Program> _tenantAdminFactory;
    private readonly HttpClient _tenantAdminClient;

    private readonly IAdminContext _instanceAdminContext;
    private readonly IAdminContext _regularUserContext;
    private readonly IAdminContext _tenantAdminContext;
    private readonly ITenantContext _tenantContext;

    private const string UnreachableByoEndpoint = "http://localhost:19998";

    private static readonly Guid DefaultTenantId = PlatformDefaults.DefaultTenantId;

    public SafeModeLatchTests(KeycloakOnlyFixture keycloak)
    {
        _keycloak = keycloak;

        _instanceAdminContext = CreateInstanceAdminContext();
        _regularUserContext = CreateRegularUserContext();
        _tenantAdminContext = CreateTenantAdminContext();
        _tenantContext = CreateTenantContext();

        var byoConfig = CreateByoCerbosConfig();

        _instanceAdminFactory = new SafeModeWebApplicationFactory(
            keycloak.Authority, keycloak.MetadataAddress,
            _instanceAdminContext, _tenantContext, byoConfig);
        _instanceAdminClient = _instanceAdminFactory.CreateClient();

        _regularUserFactory = new SafeModeWebApplicationFactory(
            keycloak.Authority, keycloak.MetadataAddress,
            _regularUserContext, _tenantContext, byoConfig);
        _regularUserClient = _regularUserFactory.CreateClient();

        _tenantAdminFactory = new SafeModeWebApplicationFactory(
            keycloak.Authority, keycloak.MetadataAddress,
            _tenantAdminContext, _tenantContext, byoConfig);
        _tenantAdminClient = _tenantAdminFactory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _instanceAdminClient.Dispose();
        _regularUserClient.Dispose();
        _tenantAdminClient.Dispose();
        await _instanceAdminFactory.DisposeAsync();
        await _regularUserFactory.DisposeAsync();
        await _tenantAdminFactory.DisposeAsync();
    }

    #region Instance Admin — Emergency Access Preserved

    [Test]
    public async Task SafeMode_InstanceAdmin_StillAllowed()
    {
        var token = await _keycloak.TokenClient.GetAdminTokenAsync();
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/instance/settings/modules", token);

        var response = await _instanceAdminClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because("instance admin emergency access is the only role preserved during safe-mode");
    }

    [Test]
    public async Task SafeMode_InstanceAdmin_CanAccessTenantResources()
    {
        var token = await _keycloak.TokenClient.GetAdminTokenAsync();
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "/api/tenant", token, CreateTenantJson());

        var response = await _instanceAdminClient.SendAsync(request);

        await Assert.That([HttpStatusCode.OK, HttpStatusCode.BadRequest]).Contains(response.StatusCode).Because("instance admin can still manage tenant resources during safe-mode");
    }

    #endregion

    #region Regular User — Denied During Safe-Mode

    [Test]
    public async Task SafeMode_RegularUser_DeniedAllAccess()
    {
        var token = await _keycloak.TokenClient.GetUserTokenAsync();
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "/api/tenant", token, CreateTenantJson());

        var response = await _regularUserClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden).Because("regular users are denied all access during safe-mode");
    }

    [Test]
    public async Task SafeMode_RegularUser_DeniedInstanceSettings()
    {
        var token = await _keycloak.TokenClient.GetUserTokenAsync();
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/instance/settings/modules", token);

        var response = await _regularUserClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden).Because("regular users cannot access instance settings during safe-mode");
    }

    #endregion

    #region Tenant Admin — Denied During Safe-Mode

    [Test]
    public async Task SafeMode_TenantAdmin_DeniedOwnTenant()
    {
        var token = await _keycloak.TokenClient.GetTenantAdminTokenAsync();
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "/api/tenant", token, CreateTenantJson());

        var response = await _tenantAdminClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden).Because("tenant admin is denied access to own tenant during safe-mode — " +
        "only instance admin emergency access is preserved");
    }

    #endregion

    #region One-Way Latch Persistence

    [Test]
    public async Task SafeMode_LatchPersistsAcrossRequests()
    {
        var adminToken = await _keycloak.TokenClient.GetAdminTokenAsync();
        var userToken = await _keycloak.TokenClient.GetUserTokenAsync();

        using var adminRequest = CreateAuthorizedRequest(HttpMethod.Get, "/api/instance/settings/modules", adminToken);
        var adminResponse = await _instanceAdminClient.SendAsync(adminRequest);
        await Assert.That(adminResponse.StatusCode).IsEqualTo(HttpStatusCode.OK).Because("instance admin access should succeed on first request");

        using var userRequest = CreateAuthorizedRequest(HttpMethod.Post, "/api/tenant", userToken, CreateTenantJson());
        var userResponse = await _regularUserClient.SendAsync(userRequest);
        await Assert.That(userResponse.StatusCode).IsEqualTo(HttpStatusCode.Forbidden).Because("regular user denied on first request after BYO Cerbos failure");

        using var userRequest2 = CreateAuthorizedRequest(HttpMethod.Post, "/api/tenant", userToken, CreateTenantJson());
        var userResponse2 = await _regularUserClient.SendAsync(userRequest2);
        await Assert.That(userResponse2.StatusCode).IsEqualTo(HttpStatusCode.Forbidden).Because("safe-mode latch persists — regular user still denied on subsequent requests");
    }

    #endregion

    #region Authentication Layer Independence

    [Test]
    public async Task SafeMode_Anonymous_StillGets401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/event/my");

        var response = await _regularUserClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized).Because("authentication (JWT validation) is independent of authorization (safe-mode)");
    }

    [Test]
    public async Task SafeMode_AnonymousEndpoints_StillWork()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/eventformat");

        var response = await _regularUserClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because("[AllowAnonymous] endpoints bypass both authentication and safe-mode authorization");
    }

    #endregion

    #region Helpers

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

    private static IAdminContext CreateInstanceAdminContext()
    {
        var ctx = Substitute.For<IAdminContext>();
        ctx.UserId.Returns(Guid.NewGuid());
        ctx.ResolveUserIdAsync(Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());
        ctx.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(true);
        ctx.IsInstanceAdminAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        ctx.IsTenantAdminAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        ctx.IsOrganizationAdminAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        ctx.GetAdminTenantIdsAsync(Arg.Any<CancellationToken>()).Returns(
            new List<Guid> { DefaultTenantId }.AsReadOnly());
        ctx.GetAdminOrganizationIdsAsync(Arg.Any<CancellationToken>()).Returns(
            new List<Guid>().AsReadOnly());
        return ctx;
    }

    private static IAdminContext CreateRegularUserContext()
    {
        var ctx = Substitute.For<IAdminContext>();
        ctx.UserId.Returns(Guid.NewGuid());
        ctx.ResolveUserIdAsync(Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());
        ctx.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        ctx.IsInstanceAdminAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        ctx.IsTenantAdminAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        ctx.IsOrganizationAdminAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        ctx.GetAdminTenantIdsAsync(Arg.Any<CancellationToken>()).Returns(
            new List<Guid>().AsReadOnly());
        ctx.GetAdminOrganizationIdsAsync(Arg.Any<CancellationToken>()).Returns(
            new List<Guid>().AsReadOnly());
        return ctx;
    }

    private static IAdminContext CreateTenantAdminContext()
    {
        var ctx = Substitute.For<IAdminContext>();
        ctx.UserId.Returns(Guid.NewGuid());
        ctx.ResolveUserIdAsync(Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());
        ctx.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        ctx.IsInstanceAdminAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        ctx.IsTenantAdminAsync(DefaultTenantId, Arg.Any<CancellationToken>()).Returns(true);
        ctx.IsTenantAdminAsync(Arg.Is<Guid>(id => id != DefaultTenantId), Arg.Any<CancellationToken>()).Returns(false);
        ctx.IsOrganizationAdminAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        ctx.GetAdminTenantIdsAsync(Arg.Any<CancellationToken>()).Returns(
            new List<Guid> { DefaultTenantId }.AsReadOnly());
        ctx.GetAdminOrganizationIdsAsync(Arg.Any<CancellationToken>()).Returns(
            new List<Guid>().AsReadOnly());
        return ctx;
    }

    private static ITenantContext CreateTenantContext()
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(DefaultTenantId);
        return ctx;
    }

    private static ICerbosConfigResolver CreateByoCerbosConfig()
    {
        var resolver = Substitute.For<ICerbosConfigResolver>();
        resolver.ResolveAsync(Arg.Any<CancellationToken>()).Returns(new CerbosConfiguration
        {
            Endpoint = UnreachableByoEndpoint,
            Mode = CerbosMode.CustomEndpoint,
            IsInstanceDefault = false
        });
        return resolver;
    }

    #endregion

    #region WebApplicationFactory

    /// <summary>
    /// WebApplicationFactory configured with a BYO Cerbos endpoint that is unreachable,
    /// with failure_mode=closed. This triggers the safe-mode latch in FallbackAuthorizationService.
    /// Uses real Keycloak JWTs for authentication and NSubstitute mocks for
    /// IAdminContext/ITenantContext/ICerbosConfigResolver.
    /// </summary>
    private sealed class SafeModeWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _keycloakAuthority;
        private readonly string _keycloakMetadataAddress;
        private readonly IAdminContext _adminContext;
        private readonly ITenantContext _tenantContext;
        private readonly ICerbosConfigResolver _cerbosConfigResolver;

        public SafeModeWebApplicationFactory(
            string keycloakAuthority,
            string keycloakMetadataAddress,
            IAdminContext adminContext,
            ITenantContext tenantContext,
            ICerbosConfigResolver cerbosConfigResolver)
        {
            _keycloakAuthority = keycloakAuthority;
            _keycloakMetadataAddress = keycloakMetadataAddress;
            _adminContext = adminContext;
            _tenantContext = tenantContext;
            _cerbosConfigResolver = cerbosConfigResolver;
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
                    ["Database:Database"] = "test_safe_mode",
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
                    ["Deployment:DefaultTenantId"] = DefaultTenantId.ToString(),
                    ["Testing:HostProfile"] = TestHostProfile.Security,
                    ["Cerbos:GrpcEndpoint"] = UnreachableByoEndpoint,
                    ["Cerbos:PlaintextMode"] = "true",
                };

                config.AddInMemoryCollection(testConfig);
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveExploreDbContextRegistrations();

                services.AddInMemoryExploreDbContext($"SafeModeDb_{Guid.NewGuid():N}");

                services.RemoveAll<IDistributedCache>();
                services.AddDistributedMemoryCache();

                services.RemoveAll<IAdminContext>();
                services.AddScoped(_ => _adminContext);

                services.RemoveAll<ITenantContext>();
                services.AddScoped(_ => _tenantContext);

                services.RemoveAll<ICerbosConfigResolver>();
                services.AddScoped(_ => _cerbosConfigResolver);
            });

            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.RequireHttpsMetadata = false;
                    options.Authority = _keycloakAuthority;
                    options.MetadataAddress = _keycloakMetadataAddress;
                    options.TokenValidationParameters.ValidIssuer = _keycloakAuthority;
                    options.TokenValidationParameters.ValidIssuers = [_keycloakAuthority];
                    options.BackchannelHttpHandler = new SocketsHttpHandler
                    {
                        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                        SslOptions = new SslClientAuthenticationOptions
                        {
                            RemoteCertificateValidationCallback = (_, _, _, sslPolicyErrors) =>
                                sslPolicyErrors == System.Net.Security.SslPolicyErrors.None
                        },
                        ConnectCallback = async (context, cancellationToken) =>
                        {
                            var socket = new System.Net.Sockets.Socket(
                                System.Net.Sockets.AddressFamily.InterNetwork,
                                System.Net.Sockets.SocketType.Stream,
                                System.Net.Sockets.ProtocolType.Tcp);
                            try
                            {
                                await socket.ConnectAsync(context.DnsEndPoint, cancellationToken);
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

    #endregion
}
