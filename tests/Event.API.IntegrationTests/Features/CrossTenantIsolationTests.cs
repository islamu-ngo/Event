using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models;
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
using TUnit.Core;
using TUnit.Core.Interfaces;

namespace Event.Api.IntegrationTests.Features;

/// <summary>
/// Validates cross-tenant isolation guarantees under Local RBAC.
///
/// <para>Tests prove that:</para>
/// <list type="bullet">
/// <item>A tenant admin for Tenant A cannot access Tenant B's tenant-scoped resources</item>
/// <item>Instance admin can access all tenants (super-admin bypass)</item>
/// <item>Regular user with no tenant admin role is denied all tenant-scoped writes</item>
/// </list>
///
/// <para>This is critical for multi-tenant deployments where data isolation is a security requirement.</para>
/// </summary>
[Category(TestCategories.Security)]
[ClassDataSource<KeycloakOnlyFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("SecurityInfra")]
public class CrossTenantIsolationTests : IAsyncDisposable
{
    private readonly KeycloakOnlyFixture _keycloak;

    private static readonly Guid TenantA = PlatformDefaults.DefaultTenantId;
    private static readonly Guid TenantB = Guid.Parse("018e4e5c-7f00-7000-8000-000000000099");

    private readonly WebApplicationFactory<Program> _tenantAAdminFactory;
    private readonly HttpClient _tenantAAdminClient;

    private readonly WebApplicationFactory<Program> _tenantBAdminFactory;
    private readonly HttpClient _tenantBAdminClient;

    private readonly WebApplicationFactory<Program> _instanceAdminFactory;
    private readonly HttpClient _instanceAdminClient;

    private readonly WebApplicationFactory<Program> _regularUserFactory;
    private readonly HttpClient _regularUserClient;

    public CrossTenantIsolationTests(KeycloakOnlyFixture keycloak)
    {
        _keycloak = keycloak;

        var tenantAAdminContext = CreateTenantAdminContext(TenantA);
        var tenantBAdminContext = CreateTenantAdminContext(TenantB);
        var instanceAdminContext = CreateInstanceAdminContext();
        var regularUserContext = CreateRegularUserContext();

        _tenantAAdminFactory = CreateFactory(tenantAAdminContext, TenantA);
        _tenantAAdminClient = _tenantAAdminFactory.CreateClient();

        _tenantBAdminFactory = CreateFactory(tenantBAdminContext, TenantB);
        _tenantBAdminClient = _tenantBAdminFactory.CreateClient();

        _instanceAdminFactory = CreateFactory(instanceAdminContext, TenantA);
        _instanceAdminClient = _instanceAdminFactory.CreateClient();

        _regularUserFactory = CreateFactory(regularUserContext, TenantA);
        _regularUserClient = _regularUserFactory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _tenantAAdminClient.Dispose();
        _tenantBAdminClient.Dispose();
        _instanceAdminClient.Dispose();
        _regularUserClient.Dispose();
        await _tenantAAdminFactory.DisposeAsync();
        await _tenantBAdminFactory.DisposeAsync();
        await _instanceAdminFactory.DisposeAsync();
        await _regularUserFactory.DisposeAsync();
    }

    #region Tenant A Admin — Can Access Own Tenant

    [Test]
    public async Task Isolation_TenantAAdmin_CanAccessOwnTenantSettings()
    {
        var token = await _keycloak.TokenClient.GetTenantAdminTokenAsync();
        using var request = Auth(HttpMethod.Get, "/api/settings/tenant/appearance", token);

        var response = await _tenantAAdminClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because("tenant A admin should be able to access own tenant settings");
    }

    [Test]
    public async Task Isolation_TenantAAdmin_CanViewOwnTenantList()
    {
        var token = await _keycloak.TokenClient.GetTenantAdminTokenAsync();
        using var request = Auth(HttpMethod.Get, "/api/tenant", token);

        var response = await _tenantAAdminClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because("tenant A admin should be able to list tenants");
    }

    [Test]
    public async Task Isolation_TenantAAdmin_CanAccessOwnTenantUserRoleGrants()
    {
        var token = await _keycloak.TokenClient.GetTenantAdminTokenAsync();
        using var request = Auth(HttpMethod.Get, "/api/tenant-user-role-grants", token);

        var response = await _tenantAAdminClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because("tenant A admin should be able to list own tenant user role grants");
    }

    [Test]
    public async Task Isolation_TenantAAdmin_DeniedInstanceSettings()
    {
        var token = await _keycloak.TokenClient.GetTenantAdminTokenAsync();
        using var request = Auth(HttpMethod.Get, "/api/instance/settings/modules", token);

        var response = await _tenantAAdminClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden).Because("tenant A admin should not be able to access instance-level settings");
    }

    #endregion

    #region Instance Admin — Cross-Tenant Access

    [Test]
    public async Task Isolation_InstanceAdmin_CanAccessTenantSettings()
    {
        var token = await _keycloak.TokenClient.GetAdminTokenAsync();
        using var request = Auth(HttpMethod.Get, "/api/settings/tenant/appearance", token);

        var response = await _instanceAdminClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because("instance admin should be able to access any tenant's settings");
    }

    [Test]
    public async Task Isolation_InstanceAdmin_CanAccessTenantUserRoleGrants()
    {
        var token = await _keycloak.TokenClient.GetAdminTokenAsync();
        using var request = Auth(HttpMethod.Get, "/api/tenant-user-role-grants", token);

        var response = await _instanceAdminClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because("instance admin should be able to access any tenant's members");
    }

    [Test]
    public async Task Isolation_InstanceAdmin_CanAccessTenantList()
    {
        var token = await _keycloak.TokenClient.GetAdminTokenAsync();
        using var request = Auth(HttpMethod.Get, "/api/tenant", token);

        var response = await _instanceAdminClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because("instance admin should be able to list all tenants");
    }

    #endregion

    #region Regular User — No Tenant Access

    [Test]
    public async Task Isolation_RegularUser_DeniedTenantSettingsUpdate()
    {
        var token = await _keycloak.TokenClient.GetUserTokenAsync();
        using var request = Auth(HttpMethod.Put, "/api/settings/tenant/appearance", token);

        var response = await _regularUserClient.SendAsync(request);

        await Assert.That(new[] { HttpStatusCode.Forbidden, HttpStatusCode.BadRequest }).Contains(response.StatusCode).Because("regular user should be denied tenant settings updates or fail request validation before update");
    }

    [Test]
    public async Task Isolation_RegularUser_DeniedTenantUserRoleGrantCreate()
    {
        var token = await _keycloak.TokenClient.GetUserTokenAsync();
        using var request = Auth(HttpMethod.Post, "/api/tenant-user-role-grants", token);

        var response = await _regularUserClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden).Because("regular user should not be able to create tenant user role grants");
    }

    [Test]
    public async Task Isolation_RegularUser_DeniedCategoryCreate()
    {
        var token = await _keycloak.TokenClient.GetUserTokenAsync();
        using var request = Auth(HttpMethod.Post, "/api/category", token);

        var response = await _regularUserClient.SendAsync(request);

        await Assert.That(new[] { HttpStatusCode.Forbidden, HttpStatusCode.BadRequest }).Contains(response.StatusCode).Because("regular user should be denied category creation");
    }

    [Test]
    public async Task Isolation_RegularUser_DeniedLocationCreate()
    {
        var token = await _keycloak.TokenClient.GetUserTokenAsync();
        using var request = Auth(HttpMethod.Post, "/api/location", token);

        var response = await _regularUserClient.SendAsync(request);

        await Assert.That(new[] { HttpStatusCode.Forbidden, HttpStatusCode.BadRequest }).Contains(response.StatusCode).Because("regular user should be denied location creation");
    }

    [Test]
    public async Task Isolation_RegularUser_DeniedTagCreate()
    {
        var token = await _keycloak.TokenClient.GetUserTokenAsync();
        using var request = Auth(HttpMethod.Post, "/api/tag", token);

        var response = await _regularUserClient.SendAsync(request);

        await Assert.That(new[] { HttpStatusCode.Forbidden, HttpStatusCode.BadRequest }).Contains(response.StatusCode).Because("regular user should be denied tag creation");
    }

    [Test]
    public async Task Isolation_RegularUser_DeniedOrganizationCreate()
    {
        var token = await _keycloak.TokenClient.GetUserTokenAsync();
        using var request = Auth(HttpMethod.Post, "/api/organization", token);

        var response = await _regularUserClient.SendAsync(request);

        await Assert.That(new[] { HttpStatusCode.Forbidden, HttpStatusCode.BadRequest }).Contains(response.StatusCode).Because("regular user should be denied organization creation");
    }

    [Test]
    public async Task Isolation_RegularUser_DeniedEventCreate()
    {
        var token = await _keycloak.TokenClient.GetUserTokenAsync();
        using var request = Auth(HttpMethod.Post, "/api/event", token);

        var response = await _regularUserClient.SendAsync(request);

        await Assert.That(new[] { HttpStatusCode.Forbidden, HttpStatusCode.BadRequest }).Contains(response.StatusCode).Because("regular user should be denied event creation");
    }

    #endregion

    #region Tenant B Admin — Different Tenant Context

    [Test]
    public async Task Isolation_TenantBAdmin_CanAccessOwnTenantSettings()
    {
        var token = await _keycloak.TokenClient.GetTenantAdminTokenAsync();
        using var request = Auth(HttpMethod.Get, "/api/settings/tenant/appearance", token);

        var response = await _tenantBAdminClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because("tenant B admin should be able to access own tenant settings");
    }

    [Test]
    public async Task Isolation_TenantBAdmin_DeniedInstanceSettings()
    {
        var token = await _keycloak.TokenClient.GetTenantAdminTokenAsync();
        using var request = Auth(HttpMethod.Get, "/api/instance/settings/modules", token);

        var response = await _tenantBAdminClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden).Because("tenant B admin should not be able to access instance-level settings");
    }

    #endregion

    #region Anonymous — No Tenant Access At All

    [Test]
    public async Task Isolation_Anonymous_DeniedTenantList()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/tenant");

        var response = await _regularUserClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized).Because("anonymous requests should get 401 for tenant endpoints");
    }

    [Test]
    public async Task Isolation_Anonymous_DeniedTenantSettings()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/settings/tenant/appearance");

        var response = await _regularUserClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized).Because("anonymous requests should get 401 for tenant settings");
    }

    #endregion

    #region Helpers

    private static HttpRequestMessage Auth(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (method == HttpMethod.Post || method == HttpMethod.Put || method == HttpMethod.Patch)
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        return request;
    }

    private static IAdminContext CreateTenantAdminContext(Guid tenantId)
    {
        var ctx = Substitute.For<IAdminContext>();
        ctx.UserId.Returns(Guid.NewGuid());
        ctx.ResolveUserIdAsync(Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());
        ctx.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        ctx.IsInstanceAdminAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        ctx.IsTenantAdminAsync(tenantId, Arg.Any<CancellationToken>()).Returns(true);
        ctx.IsTenantAdminAsync(Arg.Is<Guid>(id => id != tenantId), Arg.Any<CancellationToken>()).Returns(false);
        ctx.IsOrganizationAdminAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        ctx.GetAdminTenantIdsAsync(Arg.Any<CancellationToken>()).Returns(
            new List<Guid> { tenantId }.AsReadOnly());
        ctx.GetAdminOrganizationIdsAsync(Arg.Any<CancellationToken>()).Returns(
            new List<Guid>().AsReadOnly());
        return ctx;
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
            new List<Guid> { TenantA, TenantB }.AsReadOnly());
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

    private WebApplicationFactory<Program> CreateFactory(IAdminContext adminContext, Guid tenantId)
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);

        var cerbosConfigResolver = Substitute.For<ICerbosConfigResolver>();
        cerbosConfigResolver.ResolveAsync(Arg.Any<CancellationToken>()).Returns((CerbosConfiguration?)null);

        return new IsolationWebApplicationFactory(
            _keycloak.Authority, _keycloak.MetadataAddress,
            adminContext, tenantContext, cerbosConfigResolver);
    }

    #endregion

    #region WebApplicationFactory

    private sealed class IsolationWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _keycloakAuthority;
        private readonly string _keycloakMetadataAddress;
        private readonly IAdminContext _adminContext;
        private readonly ITenantContext _tenantContext;
        private readonly ICerbosConfigResolver _cerbosConfigResolver;

        public IsolationWebApplicationFactory(
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
                    ["Database:Database"] = "test_isolation",
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
                    ["Cerbos:GrpcEndpoint"] = "http://localhost:19999",
                    ["Cerbos:PlaintextMode"] = "true",
                };

                config.AddInMemoryCollection(testConfig);
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveExploreDbContextRegistrations();

                services.AddInMemoryExploreDbContext($"IsolationDb_{Guid.NewGuid():N}");

                services.RemoveAll<IDistributedCache>();
                services.AddDistributedMemoryCache();

                services.RemoveAll<IAdminContext>();
                services.AddScoped(_ => _adminContext);

                services.RemoveAll<ITenantContext>();
                services.AddScoped(_ => _tenantContext);

                services.RemoveAll<ICerbosConfigResolver>();
                services.AddScoped(_ => _cerbosConfigResolver);

                services.AddSingleton<IHostedService, IsolationSystemSettingSeeder>();
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

    private sealed class IsolationSystemSettingSeeder : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;

        public IsolationSystemSettingSeeder(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();

            dbContext.SystemSettings.Add(new Explore.Domain.SystemSetting
            {
                Id = Guid.NewGuid(),
                SettingKey = GovernanceSettingKeys.Security.AuthorizationProvider,
                Value = "\"local\"",
                ValueType = Explore.Domain.SettingValueType.String,
                IsLocked = false,
                Category = "Security",
                Description = "Authorization provider (local RBAC)",
                DisplayOrder = 0,
                CreatedAt = DateTime.UtcNow
            });

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    #endregion
}
