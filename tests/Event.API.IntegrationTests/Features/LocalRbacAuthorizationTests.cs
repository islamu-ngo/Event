using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Event.Testing.DataProtection;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Modules;
using Explore.Persistence;
using Explore.Persistence.Seed;
using Explore.ServiceDefaults.DataProtection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

/// <summary>
/// Validates that <see cref="Explore.Infrastructure.Services.FallbackAuthorizationService"/>
/// (Local RBAC) functions correctly as a standalone authorization provider.
///
/// <para>The RuntimeAuthorizationProvider routes to FallbackAuthorizationService when
/// <c>authorization.provider</c> is <c>"local"</c> (or unset).</para>
///
/// <para>Uses real Keycloak JWTs and persisted application users, platform roles
/// and selected-tenant grants. Tenant selection and absence of BYO configuration
/// are controlled independently of the production identity checks.</para>
/// </summary>
[Category(TestCategories.Security)]
[ClassDataSource<KeycloakOnlyFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("SecurityInfra")]
public class LocalRbacAuthorizationTests : IAsyncDisposable
{
    private readonly KeycloakOnlyFixture _keycloak;

    private readonly LocalRbacWebApplicationFactory _instanceAdminFactory;
    private readonly HttpClient _instanceAdminClient;

    private readonly WebApplicationFactory<Program> _tenantAdminFactory;
    private readonly HttpClient _tenantAdminClient;

    private readonly WebApplicationFactory<Program> _regularUserFactory;
    private readonly HttpClient _regularUserClient;

    private readonly WebApplicationFactory<Program> _anonymousFactory;
    private readonly HttpClient _anonymousClient;

    private readonly ITenantContext _tenantContext;
    private readonly ICerbosConfigResolver _cerbosConfigResolver;

    private static readonly Guid DefaultTenantId = PlatformDefaults.DefaultTenantId;

    public LocalRbacAuthorizationTests(KeycloakOnlyFixture keycloak)
    {
        _keycloak = keycloak;

        _tenantContext = CreateTenantContext();
        _cerbosConfigResolver = CreateCerbosConfigResolver();

        _instanceAdminFactory = new LocalRbacWebApplicationFactory(
            keycloak.Authority, keycloak.MetadataAddress, keycloak.TokenClient,
            _tenantContext, _cerbosConfigResolver);
        _instanceAdminClient = _instanceAdminFactory.CreateClient();

        _tenantAdminFactory = new LocalRbacWebApplicationFactory(
            keycloak.Authority, keycloak.MetadataAddress, keycloak.TokenClient,
            _tenantContext, _cerbosConfigResolver);
        _tenantAdminClient = _tenantAdminFactory.CreateClient();

        _regularUserFactory = new LocalRbacWebApplicationFactory(
            keycloak.Authority, keycloak.MetadataAddress, keycloak.TokenClient,
            _tenantContext, _cerbosConfigResolver);
        _regularUserClient = _regularUserFactory.CreateClient();

        _anonymousFactory = new LocalRbacWebApplicationFactory(
            keycloak.Authority, keycloak.MetadataAddress, keycloak.TokenClient,
            _tenantContext, _cerbosConfigResolver);
        _anonymousClient = _anonymousFactory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _instanceAdminClient.Dispose();
        _tenantAdminClient.Dispose();
        _regularUserClient.Dispose();
        _anonymousClient.Dispose();
        await _instanceAdminFactory.DisposeAsync();
        await _tenantAdminFactory.DisposeAsync();
        await _regularUserFactory.DisposeAsync();
        await _anonymousFactory.DisposeAsync();
    }

    #region Instance Admin — Full Access

    [Test]
    public async Task LocalRbac_InstanceAdmin_GetInstanceSettings()
    {
        var token = await _keycloak.TokenClient.GetAdminTokenAsync();
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/instance/settings/modules", token);

        var response = await _instanceAdminClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because("instance admin should have full access to instance settings via local RBAC");
    }

    [Test]
    public async Task LocalRbac_InstanceAdmin_CreateTenant()
    {
        var token = await _keycloak.TokenClient.GetAdminTokenAsync();
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "/api/tenant", token, CreateTenantJson());

        var response = await _instanceAdminClient.SendAsync(request);

        await Assert.That(new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest }).Contains(response.StatusCode).Because("instance admin should be allowed to create tenants via local RBAC " +
        "(actual status depends on request body validation, not authorization)");
    }

    #endregion

    #region Regular User — Restricted Access

    [Test]
    public async Task LocalRbac_RegularUser_DeniedInstanceSettings()
    {
        var token = await _keycloak.TokenClient.GetUserTokenAsync();
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/instance/settings/modules", token);

        var response = await _regularUserClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden).Because("regular user should be denied access to instance settings via local RBAC");
    }

    [Test]
    public async Task LocalRbac_RegularUser_DeniedTenantCreation()
    {
        var token = await _keycloak.TokenClient.GetUserTokenAsync();
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "/api/tenant", token, CreateTenantJson());

        var response = await _regularUserClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden).Because("regular user should be denied tenant creation via local RBAC");
    }

    #endregion

    #region Tenant Admin — Own Tenant Access

    [Test]
    public async Task LocalRbac_TenantAdmin_CanViewTenants()
    {
        var token = await _keycloak.TokenClient.GetTenantAdminTokenAsync();
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/tenant", token);

        var response = await _tenantAdminClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because("tenant admin should be allowed to view tenants via local RBAC");
    }

    [Test]
    public async Task LocalRbac_TenantAdmin_DeniedInstanceSettings()
    {
        var token = await _keycloak.TokenClient.GetTenantAdminTokenAsync();
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/instance/settings/modules", token);

        var response = await _tenantAdminClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden).Because("tenant admin should be denied access to instance settings via local RBAC");
    }

    #endregion

    #region Authentication Layer Independence

    [Test]
    public async Task LocalRbac_Anonymous_StillGets401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/event/my");

        var response = await _anonymousClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized).Because("authentication (JWT validation) is independent of authorization (local RBAC)");
    }

    [Test]
    public async Task LocalRbac_AnonymousEndpoints_StillWork()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/eventformat");

        var response = await _anonymousClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because("[AllowAnonymous] endpoints bypass both authentication and authorization");
    }

    #endregion

    #region Setting Access

    [Test]
    public async Task LocalRbac_InstanceAdmin_CanUpdateSettings()
    {
        var token = await _keycloak.TokenClient.GetAdminTokenAsync();
        using var request = CreateAuthorizedRequest(HttpMethod.Patch, "/api/instance/settings/modules", token, ModuleSettingsJson);

        var response = await _instanceAdminClient.SendAsync(request);

        await Assert.That(new[] { HttpStatusCode.OK, HttpStatusCode.NotFound }).Contains(response.StatusCode).Because("instance admin should be allowed to update settings via local RBAC " +
        "(actual status depends on whether the setting exists, not authorization); " +
        _instanceAdminFactory.RequestFailureSummary);
    }

    [Test]
    public async Task LocalRbac_RegularUser_DeniedSettingUpdate()
    {
        var token = await _keycloak.TokenClient.GetUserTokenAsync();
        using var request = CreateAuthorizedRequest(HttpMethod.Patch, "/api/instance/settings/modules", token, ModuleSettingsJson);

        var response = await _regularUserClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden).Because("regular user should be denied instance setting update via local RBAC");
    }

    #endregion

    #region Module Governance

    [Test]
    public async Task LocalRbac_TenantAdmin_CanEnableTenantModule()
    {
        var token = await _keycloak.TokenClient.GetTenantAdminTokenAsync();
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "/api/module/Mod_Tech/enable", token);

        var response = await _tenantAdminClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because("tenant admin should be allowed to enable modules for their tenant via local RBAC");
    }

    [Test]
    public async Task LocalRbac_TenantAdmin_CanDisableTenantModule()
    {
        var token = await _keycloak.TokenClient.GetTenantAdminTokenAsync();
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "/api/module/Mod_Islamic/disable", token);

        var response = await _tenantAdminClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because("tenant admin should be allowed to disable modules for their tenant via local RBAC");
    }

    [Test]
    public async Task LocalRbac_RegularUser_DeniedModuleEnable()
    {
        var token = await _keycloak.TokenClient.GetUserTokenAsync();
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "/api/module/Mod_Tech/enable", token);

        var response = await _regularUserClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden).Because("regular user should be denied module enablement via local RBAC");
    }

    [Test]
    public async Task LocalRbac_RegularUser_DeniedModuleDisable()
    {
        var token = await _keycloak.TokenClient.GetUserTokenAsync();
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "/api/module/Mod_Islamic/disable", token);

        var response = await _regularUserClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden).Because("regular user should be denied module disablement via local RBAC");
    }

    #endregion

    #region Helpers

    private const string ModuleSettingsJson = "{\"enableIslamicModule\":{\"hasValue\":true,\"value\":true},\"enableTechModule\":{\"hasValue\":true,\"value\":true}}";

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

    private static ITenantContext CreateTenantContext()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(DefaultTenantId);
        return tenantContext;
    }

    private static ICerbosConfigResolver CreateCerbosConfigResolver()
    {
        var resolver = Substitute.For<ICerbosConfigResolver>();
        resolver.ResolveAsync(Arg.Any<CancellationToken>()).Returns((CerbosConfiguration?)null);
        return resolver;
    }

    #endregion

    #region WebApplicationFactory

    /// <summary>
    /// WebApplicationFactory configured for Local RBAC authorization.
    /// Sets authorization.provider = "local" (or leaves unset, which defaults to local).
    /// Uses real Keycloak JWTs and persisted role grants. Tenant selection and
    /// ICerbosConfigResolver control scope and ensure no BYO override is attempted.
    /// </summary>
    private sealed class LocalRbacWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly KeycloakTokenClient _keycloakTokens;
        private readonly ExternalDataProtectionTestAuthority _dataProtectionAuthority = new("api");
        private readonly RequestFailureObserver _requestFailures = new();
        private readonly string _keycloakAuthority;
        private readonly string _keycloakMetadataAddress;
        private readonly ITenantContext _tenantContext;
        private readonly ICerbosConfigResolver _cerbosConfigResolver;

        internal string RequestFailureSummary => _requestFailures.Summary;

        public LocalRbacWebApplicationFactory(
            string keycloakAuthority,
            string keycloakMetadataAddress,
            KeycloakTokenClient keycloakTokens,
            ITenantContext tenantContext,
            ICerbosConfigResolver cerbosConfigResolver)
        {
            _keycloakTokens = keycloakTokens;
            _keycloakAuthority = keycloakAuthority;
            _keycloakMetadataAddress = keycloakMetadataAddress;
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
                    ["Database:Database"] = "test_local_rbac",
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
                    ["Cerbos:GrpcEndpoint"] = "http://localhost:19999",
                    ["Cerbos:PlaintextMode"] = "true",
                };

                config.AddInMemoryCollection(testConfig);
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveExploreDbContextRegistrations();

                services.AddKeycloakSecurityDbContext($"LocalRbacDb_{Guid.NewGuid():N}", _keycloakTokens, _tenantContext.TenantId);

                services.RemoveAll<IDistributedCache>();
                services.AddDistributedMemoryCache();

                services.RemoveAll<ITenantContext>();
                services.AddScoped(_ => _tenantContext);

                services.RemoveAll<ICerbosConfigResolver>();
                services.AddScoped(_ => _cerbosConfigResolver);

                services.AddSingleton<IHostedService, LocalRbacSystemSettingSeeder>();
            });

            builder.ConfigureTestServices(services =>
            {
                services.Insert(0, ServiceDescriptor.Singleton<IExceptionHandler>(_requestFailures));
                services.RemoveAll<IExternalDataProtectionSource>();
                services.AddSingleton<IExternalDataProtectionSource>(_dataProtectionAuthority);
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

        private sealed class RequestFailureObserver : IExceptionHandler
        {
            private string _summary = "No request exception metadata was observed.";

            internal string Summary => Volatile.Read(ref _summary);

            public ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception,
                CancellationToken cancellationToken)
            {
                try
                {
                    var chain = new List<string>();
                    for (Exception? current = exception; current is not null && chain.Count < 8;
                        current = current.InnerException)
                    {
                        string description = current.GetType().FullName ?? current.GetType().Name;
                        StackFrame? frame = (new StackTrace(current, fNeedFileInfo: true).GetFrames() ?? [])
                            .FirstOrDefault(candidate => candidate.GetMethod()?.DeclaringType?.Namespace is { } name
                                && (name.StartsWith("Explore.", StringComparison.Ordinal)
                                    || name.StartsWith("Event.", StringComparison.Ordinal)));
                        if (frame?.GetMethod() is { } method)
                            description += $" at {method.DeclaringType?.FullName}.{method.Name}:line {frame.GetFileLineNumber()}";
                        if (current is DbUpdateConcurrencyException concurrency)
                            description += "; entries=[" + string.Join(",", concurrency.Entries
                                .Select(entry => entry.Metadata.ClrType.FullName ?? entry.Metadata.ClrType.Name)
                                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(16)) + "]";
                        if (current is SqliteException sqlite)
                            description += $"; sqlite={sqlite.SqliteErrorCode}/{sqlite.SqliteExtendedErrorCode}";
                        chain.Add(description);
                    }
                    Volatile.Write(ref _summary, string.Join(" -> ", chain));
                }
                catch
                {
                    Volatile.Write(ref _summary, "Exception metadata unavailable.");
                }
                // Production handlers retain exception classification, response and logging.
                return ValueTask.FromResult(false);
            }
        }
    }

    /// <summary>
    /// Seeds the authorization.provider = "local" SystemSetting into the shared-memory SQLite database
    /// so RuntimeAuthorizationProvider routes to FallbackAuthorizationService.
    /// Runs as a hosted service after the app starts.
    /// </summary>
    private sealed class LocalRbacSystemSettingSeeder : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;

        public LocalRbacSystemSettingSeeder(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var tenant = await dbContext.Tenants.FindAsync([DefaultTenantId], cancellationToken);
            if (tenant is null)
                dbContext.Tenants.Add(new TenantBuilder().WithId(DefaultTenantId).Build());
            else
                tenant.TenantStatusId = (int)TenantStatusEnum.Active;

            dbContext.SystemSettings.Add(new SystemSetting
            {
                Id = Guid.NewGuid(),
                SettingKey = GovernanceSettingKeys.Security.AuthorizationProvider,
                Value = "\"local\"",
                ValueType = SettingValueType.String,
                IsLocked = false,
                Category = "Security",
                Description = "Authorization provider (local RBAC)",
                DisplayOrder = 0,
                CreatedAt = DateTime.UtcNow
            });

            await SeedModuleGovernanceAsync(dbContext, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private static async Task SeedModuleGovernanceAsync(
            ExploreDbContext dbContext,
            CancellationToken cancellationToken)
        {
            var seedTimestamp = DateTime.UtcNow;

            if (!await dbContext.ModuleDefinitions.AnyAsync(cancellationToken))
            {
                dbContext.ModuleDefinitions.AddRange(
                    new ModuleDefinition
                    {
                        Id = SeedIds.ModuleCoreId,
                        ModuleKey = "Mod_Core",
                        Name = "Core Events",
                        Description = "Basic event functionality",
                        IconName = "Event",
                        Category = "Core",
                        DisplayOrder = 0,
                        IsActive = true,
                        CreatedAt = seedTimestamp
                    },
                    new ModuleDefinition
                    {
                        Id = SeedIds.ModuleIslamicId,
                        ModuleKey = "Mod_Islamic",
                        Name = "Islamic Events",
                        Description = "Islamic-specific event features",
                        IconName = "Mosque",
                        Category = "Domain",
                        DisplayOrder = 1,
                        IsActive = true,
                        CreatedAt = seedTimestamp
                    },
                    new ModuleDefinition
                    {
                        Id = SeedIds.ModuleTechId,
                        ModuleKey = "Mod_Tech",
                        Name = "Tech Events",
                        Description = "Developer event features",
                        IconName = "Code",
                        Category = "Domain",
                        DisplayOrder = 2,
                        IsActive = true,
                        CreatedAt = seedTimestamp
                    });
            }

            if (!await dbContext.TenantCapabilities.AnyAsync(
                    capability => capability.TenantId == DefaultTenantId &&
                        capability.ModuleId == SeedIds.ModuleIslamicId,
                    cancellationToken))
            {
                dbContext.TenantCapabilities.Add(new TenantCapability
                {
                    Id = SeedIds.DefaultTenantIslamicCapabilityId,
                    TenantId = DefaultTenantId,
                    Tenant = null!,
                    ModuleId = SeedIds.ModuleIslamicId,
                    Module = null!,
                    IsEnabled = true,
                    EnabledAt = seedTimestamp
                });
            }
        }
    }

    #endregion
}
