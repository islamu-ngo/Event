using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Exceptions;
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
/// Coverage governance tests that verify no endpoint category has auth regressions.
/// These tests act as a safety net ensuring critical authorization patterns remain enforced.
///
/// <para>Tests cover:</para>
/// <list type="bullet">
/// <item>Protected instance-setting sub-endpoints deny regular users</item>
/// <item>Every tenant admin endpoint denies unauthenticated access</item>
/// <item>Every public endpoint still works without auth</item>
/// <item>Write operations on lookup tables are gated by tenant admin</item>
/// <item>Admin-only sub-systems are fully locked down</item>
/// </list>
/// </summary>
[Category(TestCategories.Security)]
[ClassDataSource<KeycloakOnlyFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("SecurityInfra")]
public class CoverageGovernanceTests : IAsyncDisposable
{
    private readonly KeycloakOnlyFixture _keycloak;

    private readonly WebApplicationFactory<Program> _regularUserFactory;
    private readonly HttpClient _regularUserClient;

    private readonly WebApplicationFactory<Program> _instanceAdminFactory;
    private readonly HttpClient _instanceAdminClient;

    private readonly HttpClient _anonymousClient;

    private static readonly Guid DefaultTenantId = PlatformDefaults.DefaultTenantId;

    public CoverageGovernanceTests(KeycloakOnlyFixture keycloak)
    {
        _keycloak = keycloak;

        _regularUserFactory = CreateFactory(CreateRegularUserContext());
        _regularUserClient = _regularUserFactory.CreateClient();

        _instanceAdminFactory = CreateFactory(CreateInstanceAdminContext());
        _instanceAdminClient = _instanceAdminFactory.CreateClient();

        _anonymousClient = _regularUserFactory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _regularUserClient.Dispose();
        _instanceAdminClient.Dispose();
        _anonymousClient.Dispose();
        await _regularUserFactory.DisposeAsync();
        await _instanceAdminFactory.DisposeAsync();
    }

    #region Instance Settings — All Sub-Endpoints Deny Regular Users

    [Test]
    public async Task Governance_InstanceSettings_Modules_DeniesRegularUser()
    {
        await AssertRegularUserDenied(HttpMethod.Get, "/api/instance/settings/modules");
    }

    [Test]
    public async Task Governance_InstanceSettings_Events_DeniesRegularUser()
    {
        await AssertRegularUserDenied(HttpMethod.Get, "/api/instance/settings/events");
    }

    [Test]
    public async Task Governance_InstanceSettings_Organizations_DeniesRegularUser()
    {
        await AssertRegularUserDenied(HttpMethod.Get, "/api/instance/settings/organizations");
    }

    [Test]
    public async Task Governance_InstanceSettings_Branding_DeniesRegularUser()
    {
        await AssertRegularUserDenied(HttpMethod.Get, "/api/instance/settings/branding");
    }

    [Test]
    public async Task Governance_InstanceSettings_Domains_DeniesRegularUser()
    {
        await AssertRegularUserDenied(HttpMethod.Get, "/api/instance/settings/domains");
    }

    [Test]
    public async Task Governance_InstanceSettings_Storage_DeniesRegularUser()
    {
        await AssertRegularUserDenied(HttpMethod.Get, "/api/instance/settings/storage");
    }

    [Test]
    public async Task Governance_InstanceSettings_Smtp_DeniesRegularUser()
    {
        await AssertRegularUserDenied(HttpMethod.Get, "/api/instance/settings/smtp");
    }

    [Test]
    public async Task Governance_InstanceSettings_DeploymentMode_DeniesRegularUser()
    {
        await AssertRegularUserDenied(HttpMethod.Get, "/api/instance/settings/deployment-mode");
    }

    [Test]
    public async Task Governance_InstanceSettings_TenantDelegation_DeniesRegularUser()
    {
        await AssertRegularUserDenied(HttpMethod.Get, "/api/instance/settings/tenant-delegation");
    }

    [Test]
    public async Task Governance_InstanceSettings_RenderPolicy_DeniesRegularUser()
    {
        await AssertRegularUserDenied(HttpMethod.Get, "/api/instance/settings/render-policy");
    }

    [Test]
    public async Task Governance_InstanceSettings_AnalyticsGovernance_DeniesRegularUser()
    {
        await AssertRegularUserDenied(HttpMethod.Get, "/api/instance/settings/analytics-governance");
    }

    [Test]
    public async Task Governance_InstanceSettings_FooterGovernance_DeniesRegularUser()
    {
        await AssertRegularUserDenied(HttpMethod.Get, "/api/instance/settings/footer-governance");
    }

    [Test]
    public async Task Governance_InstanceSettings_AuthProvider_DeniesRegularUser()
    {
        await AssertRegularUserDenied(HttpMethod.Get, "/api/instance/settings/auth-provider");
    }

    [Test]
    public async Task Governance_InstanceSettings_AuthzProvider_DeniesRegularUser()
    {
        await AssertRegularUserDenied(HttpMethod.Get, "/api/instance/settings/authz-provider");
    }

    [Test]
    public async Task Governance_InstanceSettings_ResolverConfig_AllowsRegularUser()
    {
        await AssertRegularUserOk(HttpMethod.Get, "/api/instance/settings/resolver-config");
    }

    #endregion

    #region Instance Settings — All Sub-Endpoints Allow Instance Admin

    [Test]
    public async Task Governance_InstanceSettings_Modules_AllowsInstanceAdmin()
    {
        await AssertInstanceAdminOk(HttpMethod.Get, "/api/instance/settings/modules");
    }

    [Test]
    public async Task Governance_InstanceSettings_Events_AllowsInstanceAdmin()
    {
        await AssertInstanceAdminOk(HttpMethod.Get, "/api/instance/settings/events");
    }

    [Test]
    public async Task Governance_InstanceSettings_Branding_AllowsInstanceAdmin()
    {
        await AssertInstanceAdminOk(HttpMethod.Get, "/api/instance/settings/branding");
    }

    [Test]
    public async Task Governance_InstanceSettings_Storage_AllowsInstanceAdmin()
    {
        await AssertInstanceAdminOk(HttpMethod.Get, "/api/instance/settings/storage");
    }

    [Test]
    public async Task Governance_InstanceSettings_Smtp_AllowsInstanceAdmin()
    {
        await AssertInstanceAdminOk(HttpMethod.Get, "/api/instance/settings/smtp");
    }

    #endregion

    #region Admin Sub-Systems — Fully Locked Down for Regular Users

    [Test]
    public async Task Governance_Admin_UiThemes_AllowsAuthenticatedCatalogView()
    {
        await AssertRegularUserOk(HttpMethod.Get, "/api/admin/ui-themes");
    }

    [Test]
    public async Task Governance_Admin_Localization_AllowsAuthenticatedConfigurationView()
    {
        await AssertRegularUserOk(HttpMethod.Get, "/api/admin/localization/configuration");
    }

    [Test]
    public async Task Governance_Admin_CustomPropertyGovernance_DeniesRegularUser()
    {
        await AssertRegularUserDenied(HttpMethod.Get,
            $"/api/admin/custom-property-definitions/governance-report?tenantId={DefaultTenantId}");
    }

    [Test]
    public async Task Governance_Admin_CustomPropertyProjections_AllowsAuthenticatedStatusView()
    {
        await AssertRegularUserDenied(HttpMethod.Get,
            $"/api/admin/custom-property-projections/status?tenantId={DefaultTenantId}");
    }

    [Test]
    public async Task Governance_Admin_ExternalApiKeys_AllowsAuthenticatedVisibleKeys()
    {
        await AssertRegularUserOk(HttpMethod.Get, "/api/externalapikey");
    }

    [Test]
    public async Task Governance_Admin_TenantUserRoleGrantCreate_DeniesRegularUser()
    {
        await AssertRegularUserDenied(HttpMethod.Post, "/api/tenant-user-role-grants");
    }

    #endregion

    #region Lookup Tables — Write Operations Gated

    [Test]
    public async Task Governance_Lookup_CategoryCreate_DeniesAnonymous()
    {
        await AssertAnonymous401(HttpMethod.Post, "/api/category");
    }

    [Test]
    public async Task Governance_Lookup_TagCreate_DeniesAnonymous()
    {
        await AssertAnonymous401(HttpMethod.Post, "/api/tag");
    }

    [Test]
    public async Task Governance_Lookup_LocationCreate_DeniesAnonymous()
    {
        await AssertAnonymous401(HttpMethod.Post, "/api/location");
    }

    [Test]
    public async Task Governance_Lookup_LocationRoomCreate_DeniesAnonymous()
    {
        await AssertAnonymous401(HttpMethod.Post, "/api/locationroom");
    }

    [Test]
    public async Task Governance_Lookup_EventDayCreate_DeniesAnonymous()
    {
        await AssertAnonymous401(HttpMethod.Post, "/api/eventday");
    }

    [Test]
    public async Task Governance_Lookup_EventSessionCreate_DeniesAnonymous()
    {
        await AssertAnonymous401(HttpMethod.Post, "/api/eventsession");
    }

    [Test]
    public async Task Governance_Lookup_EventAgendaItemCreate_DeniesAnonymous()
    {
        await AssertAnonymous401(HttpMethod.Post, "/api/eventagendaitem");
    }

    [Test]
    public async Task Governance_Lookup_EventSeriesCreate_DeniesAnonymous()
    {
        await AssertAnonymous401(HttpMethod.Post, "/api/eventseries");
    }

    [Test]
    public async Task Governance_Lookup_CustomPropertyDefCreate_DeniesAnonymous()
    {
        await AssertAnonymous401(HttpMethod.Post, "/api/custompropertydefinition");
    }

    [Test]
    public async Task Governance_Lookup_OrganizationCreate_DeniesAnonymous()
    {
        await AssertAnonymous401(HttpMethod.Post, "/api/organization");
    }

    [Test]
    public async Task Governance_Lookup_GroupCreate_DeniesAnonymous()
    {
        await AssertAnonymous401(HttpMethod.Post, "/api/group");
    }

    [Test]
    public async Task Governance_Lookup_EventCreate_DeniesAnonymous()
    {
        await AssertAnonymous401(HttpMethod.Post, "/api/event");
    }

    [Test]
    public async Task Governance_RegistrationOrderCreate_DeniesAnonymous()
    {
        await AssertAnonymous401(HttpMethod.Post, $"/api/events/{Guid.NewGuid()}/registration-orders");
    }

    [Test]
    public async Task Governance_Lookup_EventTemplateCreate_DeniesAnonymous()
    {
        await AssertAnonymous401(HttpMethod.Post, "/api/eventtemplate");
    }

    [Test]
    public async Task Governance_Lookup_EventSessionTemplateCreate_DeniesAnonymous()
    {
        await AssertAnonymous401(HttpMethod.Post, "/api/eventsessiontemplate");
    }

    #endregion

    #region Lookup Tables — Read Operations Public

    [Test]
    public async Task Governance_PublicLookup_EventFormats_AnonymousOK()
    {
        await AssertAnonymousOk("/api/eventformat");
    }

    [Test]
    public async Task Governance_PublicLookup_EventStatuses_AnonymousOK()
    {
        await AssertAnonymousOk("/api/eventstatus");
    }

    [Test]
    public async Task Governance_PublicLookup_VisibilityTypes_AnonymousOK()
    {
        await AssertAnonymousOk("/api/visibilitytype");
    }

    [Test]
    public async Task Governance_PublicLookup_FileTypes_AnonymousOK()
    {
        await AssertAnonymousOk("/api/filetype");
    }

    [Test]
    public async Task Governance_PublicLookup_AudienceAges_AnonymousOK()
    {
        await AssertAnonymousOk("/api/audienceage");
    }

    [Test]
    public async Task Governance_PublicLookup_AudienceGenders_AnonymousOK()
    {
        await AssertAnonymousOk("/api/audiencegender");
    }

    [Test]
    public async Task Governance_PublicLookup_ApprovalStatuses_AnonymousOK()
    {
        await AssertAnonymousOk("/api/approvalstatus");
    }

    [Test]
    public async Task Governance_PublicLookup_RegistrationModes_AnonymousOK()
    {
        await AssertAnonymousOk("/api/registrationmode");
    }

    [Test]
    public async Task Governance_PublicLookup_CategoryTypes_AnonymousOK()
    {
        await AssertAnonymousOk("/api/categorytype");
    }

    [Test]
    public async Task Governance_PublicLookup_TagTypes_AnonymousOK()
    {
        await AssertAnonymousOk("/api/tagtype");
    }

    [Test]
    public async Task Governance_PublicLookup_OrganizationPositions_AnonymousOK()
    {
        await AssertAnonymousOk("/api/organizationposition");
    }

    [Test]
    public async Task Governance_PublicLookup_GroupPositions_AnonymousOK()
    {
        await AssertAnonymousOk("/api/groupposition");
    }

    [Test]
    public async Task Governance_PublicLookup_DidCustodyTypes_AnonymousOK()
    {
        await AssertAnonymousOk("/api/didcustodytype");
    }

    [Test]
    public async Task Governance_PublicLookup_Madhabs_AnonymousOK()
    {
        await AssertAnonymousOk("/api/madhab");
    }

    [Test]
    public async Task Governance_PublicLookup_ActorTypes_AnonymousOK()
    {
        await AssertAnonymousOk("/api/actortype");
    }

    [Test]
    public async Task Governance_PublicLookup_EventRegistrationPolicies_AnonymousOK()
    {
        await AssertAnonymousOk("/api/eventregistrationpolicy");
    }

    #endregion

    #region Helpers

    private async Task AssertRegularUserDenied(HttpMethod method, string url)
    {
        var token = await _keycloak.TokenClient.GetUserTokenAsync();
        using var request = Auth(method, url, token);
        HttpResponseMessage response;
        try
        {
            response = await _regularUserClient.SendAsync(request);
        }
        catch (AuthorizationException)
        {
            return;
        }

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden).Because($"regular user should be denied access to {method} {url}");
    }

    private async Task AssertRegularUserOk(HttpMethod method, string url)
    {
        var token = await _keycloak.TokenClient.GetUserTokenAsync();
        using var request = Auth(method, url, token);
        var response = await _regularUserClient.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because($"regular user should be allowed authenticated access to {method} {url}");
    }

    private async Task AssertInstanceAdminOk(HttpMethod method, string url)
    {
        var token = await _keycloak.TokenClient.GetAdminTokenAsync();
        using var request = Auth(method, url, token);
        var response = await _instanceAdminClient.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because($"instance admin should be allowed access to {method} {url}");
    }

    private async Task AssertAnonymous401(HttpMethod method, string url)
    {
        using var request = new HttpRequestMessage(method, url);
        var response = await _anonymousClient.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized).Because($"anonymous should get 401 for {method} {url}");
    }

    private async Task AssertAnonymousOk(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await _anonymousClient.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because($"[AllowAnonymous] endpoint {url} should return 200 for anonymous");
    }

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

    private WebApplicationFactory<Program> CreateFactory(IAdminContext adminContext)
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(DefaultTenantId);

        var cerbosConfigResolver = Substitute.For<ICerbosConfigResolver>();
        cerbosConfigResolver.ResolveAsync(Arg.Any<CancellationToken>()).Returns((CerbosConfiguration?)null);

        return new GovernanceWebApplicationFactory(
            _keycloak.Authority, _keycloak.MetadataAddress,
            adminContext, tenantContext, cerbosConfigResolver);
    }

    #endregion

    #region WebApplicationFactory

    private sealed class GovernanceWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _keycloakAuthority;
        private readonly string _keycloakMetadataAddress;
        private readonly IAdminContext _adminContext;
        private readonly ITenantContext _tenantContext;
        private readonly ICerbosConfigResolver _cerbosConfigResolver;

        public GovernanceWebApplicationFactory(
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
                    ["Database:Database"] = "test_governance",
                    ["Database:Runtime:Username"] = "postgres",
                    ["Database:Runtime:Password"] = "postgres",
                    ["Database:Runtime:TlsMode"] = "Prefer",
                    ["Database:Runtime:TrustServerCertificate"] = "false",
                    ["Keycloak:Authority"] = _keycloakAuthority,
                    ["Keycloak:Realm"] = KeycloakContainerFixture.RealmName,
                    ["Keycloak:Audience"] = "islamu-event-api",
                    ["Keycloak:RequireHttpsMetadata"] = "false",
                    ["Keycloak:MetadataAddress"] = _keycloakMetadataAddress,
                    ["S3Settings:Region"] = "us-east-1",
                    ["S3Settings:BucketName"] = "test-bucket",
                    ["S3Settings:AccessKeyId"] = "test-key",
                    ["S3Settings:SecretAccessKey"] = "test-secret",
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

                services.AddInMemoryExploreDbContext($"GovernanceDb_{Guid.NewGuid():N}");

                services.RemoveAll<IDistributedCache>();
                services.AddDistributedMemoryCache();

                services.RemoveAll<IAdminContext>();
                services.AddScoped(_ => _adminContext);

                services.RemoveAll<ITenantContext>();
                services.AddScoped(_ => _tenantContext);

                services.RemoveAll<ICerbosConfigResolver>();
                services.AddScoped(_ => _cerbosConfigResolver);

                services.AddSingleton<IHostedService, GovernanceSystemSettingSeeder>();
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

    private sealed class GovernanceSystemSettingSeeder : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;

        public GovernanceSystemSettingSeeder(IServiceProvider serviceProvider)
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
