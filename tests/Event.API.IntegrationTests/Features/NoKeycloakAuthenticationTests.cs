using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using Event.Api.IntegrationTests.Fixtures;
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
using Microsoft.IdentityModel.Tokens;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

/// <summary>
/// Verifies the API's behavior when no Keycloak OIDC configuration is present.
/// This simulates a self-hoster who has not yet configured authentication.
///
/// Core invariant: the API must not crash, must not accidentally allow access,
/// and must return 401 for all protected endpoints. Anonymous endpoints must
/// continue to function normally.
/// </summary>
[Category(TestCategories.Fast)]
public class NoKeycloakAuthenticationTests : IAsyncDisposable
{
    private readonly NoKeycloakWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public NoKeycloakAuthenticationTests()
    {
        _factory = new NoKeycloakWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    #region Protected Endpoints — All Rejected

    [Test]
    public async Task NoAuthority_Anonymous_RejectedOnProtectedEndpoint()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/event/my");

        var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized).Because("without any OIDC authority, anonymous requests to protected endpoints must get 401");
    }

    [Test]
    public async Task NoAuthority_ArbitraryJwt_Rejected()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/event/my");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJ0ZXN0In0.fake");

        var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized).Because("arbitrary JWTs must be rejected when no authority is configured to validate them");
    }

    [Test]
    public async Task NoAuthority_ExpiredToken_Rejected()
    {
        var expiredJwt = "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9." +
                         "eyJzdWIiOiJ0ZXN0LWFkbWluIiwiZXhwIjoxMDAwMDAwMDAwfQ." +
                         "fake-signature";

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/event/my");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", expiredJwt);

        var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized).Because("expired tokens must be rejected when no authority can validate them");
    }

    #endregion

    #region Anonymous Endpoints — Still Work

    [Test]
    public async Task NoAuthority_AnonymousEndpoints_StillWork()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/eventformat");

        var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because("[AllowAnonymous] endpoints must function regardless of OIDC configuration");
    }

    [Test]
    public async Task NoAuthority_InstanceOnboardingStatus_StillAccessible()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/InstanceOnboarding/status");

        var response = await _client.SendAsync(request);

        await Assert.That([HttpStatusCode.OK, HttpStatusCode.NotFound]).Contains(response.StatusCode).Because("onboarding status must be accessible for initial setup before Keycloak is configured");
    }

    [Test]
    public async Task NoAuthority_HealthEndpoint_StillAccessible()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");

        var response = await _client.SendAsync(request);

        await Assert.That([HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable]).Contains(response.StatusCode).Because("health endpoints must be accessible for infrastructure monitoring");
    }

    [Test]
    public async Task NoAuthority_AliveEndpoint_ReturnsOk()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/alive");

        var response = await _client.SendAsync(request);

        await Assert.That([HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable]).Contains(response.StatusCode).Because("liveness must stay independent from readiness-only dependencies, while still allowing the documented graceful-shutdown window to report 503");
    }

    #endregion

    #region Startup Stability

    [Test]
    public async Task NoAuthority_ServerStartsWithoutCrash()
    {
        await Assert.That(_factory).IsNotNull().Because("the factory must build successfully without Keycloak config");
        await Assert.That(_client).IsNotNull().Because("the HTTP client must be created without errors");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/eventtypes");
        var response = await _client.SendAsync(request);

        await Assert.That(response).IsNotNull().Because("the server must respond to requests even without OIDC config");
    }

    #endregion

    /// <summary>
    /// WebApplicationFactory with NO Keycloak configuration at all.
    /// Simulates a fresh deployment where OIDC has not been configured yet.
    /// </summary>
    private sealed class NoKeycloakWebApplicationFactory : WebApplicationFactory<Program>
    {
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
                    ["Database:Database"] = "test_no_keycloak",
                    ["Database:Runtime:Username"] = "postgres",
                    ["Database:Runtime:Password"] = "postgres",
                    ["Database:Runtime:TlsMode"] = "Prefer",
                    ["Database:Runtime:TrustServerCertificate"] = "false",
                    ["S3Settings:Region"] = "us-east-1",
                    ["S3Settings:BucketName"] = "test-bucket",
                    ["S3Settings:AccessKeyId"] = "test-key",
                    ["S3Settings:SecretAccessKey"] = "test-secret",
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

                services.AddInMemoryExploreDbContext($"NoKeycloakDb_{Guid.NewGuid():N}");

                services.RemoveAll<IDistributedCache>();
                services.AddDistributedMemoryCache();
            });

            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.RequireHttpsMetadata = false;
                    options.BackchannelHttpHandler = new SocketsHttpHandler
                    {
                        SslOptions = new SslClientAuthenticationOptions
                        {
                            RemoteCertificateValidationCallback = (_, _, _, sslPolicyErrors) =>
                                sslPolicyErrors == System.Net.Security.SslPolicyErrors.None
                        }
                    };
                });
            });
        }
    }
}
