using System.Net.Security;
using System.Net.Sockets;
using System.Threading.Channels;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
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

namespace Event.Api.IntegrationTests.Fixtures;

/// <summary>
/// WebApplicationFactory configured for security integration tests.
/// Uses real JWT Bearer authentication against a containerized Keycloak instance.
/// Requires a <see cref="SecurityInfrastructureFixture"/> to provide container endpoints.
/// </summary>
public class SecurityWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _keycloakAuthority;
    private readonly string _keycloakMetadataAddress;
    private readonly string _cerbosGrpcEndpoint;

    /// <summary>
    /// When non-null, replaces the real IAuthorizationProvider with this instance.
    /// For pure authentication tests, set to a StubAuthorizationProvider with AllowAll=true.
    /// For full security tests, leave null to use the real Cerbos provider.
    /// </summary>
    public IAuthorizationProvider? AuthorizationProviderOverride { get; set; }

    public DeploymentMode DeploymentMode { get; set; } = DeploymentMode.SingleTenant;

    public SecurityWebApplicationFactory(
        string keycloakAuthority,
        string keycloakMetadataAddress,
        string cerbosGrpcEndpoint)
    {
        _keycloakAuthority = keycloakAuthority;
        _keycloakMetadataAddress = keycloakMetadataAddress;
        _cerbosGrpcEndpoint = cerbosGrpcEndpoint;
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
                ["Database:Database"] = "explore_db_test",
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
                ["Deployment:Mode"] = DeploymentMode.ToString(),
                ["Deployment:DefaultTenantId"] = PlatformDefaults.DefaultTenantId.ToString(),
                ["Testing:HostProfile"] = TestHostProfile.Security,
                ["Cerbos:GrpcEndpoint"] = _cerbosGrpcEndpoint,
            };

            config.AddInMemoryCollection(testConfig);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveExploreDbContextRegistrations();

            services.AddInMemoryExploreDbContext($"InMemoryDbForSecurityTesting_{Guid.NewGuid():N}");

            services.RemoveAll<IDistributedCache>();
            services.AddDistributedMemoryCache();
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDeploymentModeProvider>();
            services.AddSingleton<IDeploymentModeProvider>(new FixedDeploymentModeProvider(DeploymentMode));

            // DO NOT override authentication — we want real JWT Bearer validation
            // pointing to the Keycloak container's OIDC metadata endpoint.
            // The app's AddJwtBearer reads Authority/MetadataAddress from config,
            // which we've overridden to point to the container.

            // Override the backchannel handler to allow plain HTTP to the container
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
                        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        try
                        {
                            await socket.ConnectAsync(context.DnsEndPoint, cancellationToken);
                            return new NetworkStream(socket, ownsSocket: true);
                        }
                        catch
                        {
                            socket.Dispose();
                            throw;
                        }
                    }
                };
            });

            // Replace IAuthorizationProvider if override provided
            if (AuthorizationProviderOverride is not null)
            {
                services.RemoveAll<IAuthorizationProvider>();
                services.AddSingleton(AuthorizationProviderOverride);
            }
        });
    }

    public override async ValueTask DisposeAsync()
    {
        try
        {
            await base.DisposeAsync();
        }
        catch (ChannelClosedException)
        {
            // OpenFeature shutdown can race WebApplicationFactory disposal after assertions complete.
        }
        catch (NullReferenceException)
        {
            // Keep parity with other integration-test factories that tolerate legacy host teardown races.
        }
        catch (ObjectDisposedException)
        {
            // Providers may already be disposed when the test host is shutting down.
        }
    }

    private sealed class FixedDeploymentModeProvider(DeploymentMode mode) : IDeploymentModeProvider
    {
        public Task<DeploymentMode> GetCurrentModeAsync(CancellationToken ct = default) =>
            Task.FromResult(mode);

        public Task<DeploymentMode> GetConfiguredOnboardingModeAsync(CancellationToken ct = default) =>
            Task.FromResult(mode);

        public Task<bool> IsSingleTenantAsync(CancellationToken ct = default) =>
            Task.FromResult(mode == DeploymentMode.SingleTenant);

        public Task InvalidateCacheAsync() => Task.CompletedTask;
    }
}
