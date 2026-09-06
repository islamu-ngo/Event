// ABOUTME: Enterprise-grade production guardrail tests proving that test-only authorization
// ABOUTME: infrastructure (StubAuthorizationProvider, AuthorizationProviderOverride) cannot
// ABOUTME: accidentally run in production environments.

using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
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
/// Production safety rail tests. These verify that test-only authorization shortcuts
/// (StubAuthorizationProvider, allow-all bypasses) cannot be accidentally used in production.
///
/// These are architecture/governance tests, not runtime tests. They prove that the DI
/// container wiring is safe: the production RuntimeAuthorizationProvider is always used
/// outside the Testing environment, and no allow-all stub can be injected accidentally.
/// </summary>
[Category(TestCategories.Fast)]
// Exclusive rather than key-scoped: proving production wiring requires a non-Testing host, whose
// bootstrap contract is satisfied through process environment values. A key-scoped constraint would
// still let unrelated tests build hosts concurrently and observe that shared state.
[NotInParallel]
public class AuthorizationProductionGuardrailTests
{
    [Test]
    public async Task AllowAllStub_NotRegisteredInNonTestingEnvironment()
    {
        using var factory = new ProductionLikeWebApplicationFactory();
        using var scope = factory.Services.CreateScope();

        var provider = scope.ServiceProvider.GetService<IAuthorizationProvider>();

        await Assert.That(provider).IsNotNull().Because("IAuthorizationProvider must always be registered");
        await Assert.That(provider).IsNotTypeOf<StubAuthorizationProvider>().Because("StubAuthorizationProvider must never be registered in a non-Testing environment");
        await Assert.That(provider!.GetType().Name).IsNotEqualTo("StubAuthorizationProvider");
    }

    [Test]
    public async Task MissingCerbosEndpoint_DoesNotDefaultToAllowAll()
    {
        using var factory = new ProductionLikeWebApplicationFactory();
        using var scope = factory.Services.CreateScope();

        var provider = scope.ServiceProvider.GetRequiredService<IAuthorizationProvider>();

        var result = await provider.AuthorizeAsync(new AuthorizationRequest(
            ResourceKinds.Event,
            "test-resource",
            AuthorizationActions.Create));

        await Assert.That(result.IsAllowed).IsFalse().Because("with no Cerbos endpoint and authorization.provider unset, " +
        "the provider must default to denying, not allowing");
    }

    [Test]
    public async Task MissingKeycloakAuthority_DoesNotAllowAnonymousSuccess()
    {
        using var factory = new NoAuthConfigWebApplicationFactory();
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/event/my");

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(System.Net.HttpStatusCode.Unauthorized).Because("without Keycloak authority configured, no requests should succeed");
    }

    [Test]
    public async Task SecurityWebApplicationFactory_AlwaysUsesTestingEnvironment()
    {
        var factory = new SecurityWebApplicationFactory(
            "http://localhost:1",
            "http://localhost:1/.well-known/openid-configuration",
            "http://localhost:19999");

        try
        {
            await Assert.That(factory.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>()
                .EnvironmentName).IsEqualTo("Testing").Because("SecurityWebApplicationFactory must always set the Testing environment " +
            "to prevent test infrastructure from leaking into production behavior");
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    /// <summary>
    /// Factory that mimics production as closely as possible (no Testing env overrides).
    /// Uses a non-Development, non-Testing environment so development configuration
    /// providers and test-only application branches cannot affect the production proof.
    /// </summary>
    private sealed class ProductionLikeWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly BootstrapAuthorityEnvironmentScope _bootstrapAuthority = new();

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                _bootstrapAuthority.Dispose();
            }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("ProductionGuardrail");
            ConfigureEarlyHostSettings(builder);

            builder.ConfigureAppConfiguration((_, config) =>
            {
                var testConfig = new Dictionary<string, string?>
                {
                    ["Database:Provider"] = "PostgreSql",
                    ["Database:Host"] = "localhost",
                    ["Database:Port"] = "5432",
                    ["Database:Database"] = "test_guardrails",
                    ["SecretProvider:Provider"] = "Environment",
                    ["Database:Runtime:TlsMode"] = "Prefer",
                    ["Database:Runtime:TrustServerCertificate"] = "false",
                    ["S3Settings:Region"] = "us-east-1",
                    ["S3Settings:BucketName"] = "test-bucket",
                    ["S3Settings:Endpoint"] = "https://s3.example.com",
                    ["Deployment:Mode"] = "SingleTenant",
                    ["Deployment:DefaultTenantId"] = PlatformDefaults.DefaultTenantId.ToString(),
                    ["Cerbos:GrpcEndpoint"] = "http://localhost:19999",
                    ["Cerbos:PlaintextMode"] = "true",
                    ["EmailDispatchProcessor:Enabled"] = "false",
                    ["Scheduler:Quartz:Enabled"] = "false",
                    ["WebhookDeliveryProcessor:Enabled"] = "false",
                    ["IncomingWebhookProcessing:Enabled"] = "false",
                    ["HttpsRedirection:Enabled"] = "false",
                    ["Testing:SkipJwtAuthorityWarmup"] = "true",
                };

                config.AddInMemoryCollection(testConfig);
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveExploreDbContextRegistrations();

                services.AddInMemoryExploreDbContext($"GuardrailDb_{Guid.NewGuid():N}");

                services.RemoveAll<IDistributedCache>();
                services.AddDistributedMemoryCache();
            });

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPrivacyErasureReplayService>();
                services.AddScoped<IPrivacyErasureReplayService, SuccessfulPrivacyErasureReplay>();
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

    /// <summary>
    /// Factory with zero authentication/authorization configuration.
    /// No Keycloak, no Cerbos, no test auth overrides.
    /// </summary>
    private sealed class NoAuthConfigWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly BootstrapAuthorityEnvironmentScope _bootstrapAuthority = new();

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                _bootstrapAuthority.Dispose();
            }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("ProductionGuardrail");
            ConfigureEarlyHostSettings(builder);

            builder.ConfigureAppConfiguration((_, config) =>
            {
                var testConfig = new Dictionary<string, string?>
                {
                    ["Database:Provider"] = "PostgreSql",
                    ["Database:Host"] = "localhost",
                    ["Database:Port"] = "5432",
                    ["Database:Database"] = "test_no_auth",
                    ["SecretProvider:Provider"] = "Environment",
                    ["Database:Runtime:TlsMode"] = "Prefer",
                    ["Database:Runtime:TrustServerCertificate"] = "false",
                    ["S3Settings:Region"] = "us-east-1",
                    ["S3Settings:BucketName"] = "test-bucket",
                    ["S3Settings:Endpoint"] = "https://s3.example.com",
                    ["Deployment:Mode"] = "SingleTenant",
                    ["Deployment:DefaultTenantId"] = PlatformDefaults.DefaultTenantId.ToString(),
                    ["Cerbos:GrpcEndpoint"] = "http://localhost:19999",
                    ["Cerbos:PlaintextMode"] = "true",
                    ["EmailDispatchProcessor:Enabled"] = "false",
                    ["Scheduler:Quartz:Enabled"] = "false",
                    ["WebhookDeliveryProcessor:Enabled"] = "false",
                    ["IncomingWebhookProcessing:Enabled"] = "false",
                    ["HttpsRedirection:Enabled"] = "false",
                    ["Testing:SkipJwtAuthorityWarmup"] = "true",
                };

                config.AddInMemoryCollection(testConfig);
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveExploreDbContextRegistrations();

                services.AddInMemoryExploreDbContext($"NoAuthDb_{Guid.NewGuid():N}");

                services.RemoveAll<IDistributedCache>();
                services.AddDistributedMemoryCache();
            });

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPrivacyErasureReplayService>();
                services.AddScoped<IPrivacyErasureReplayService, SuccessfulPrivacyErasureReplay>();
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

    private sealed class SuccessfulPrivacyErasureReplay : IPrivacyErasureReplayService
    {
        public Task ReplayAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Outside Development and Testing the host selects exactly one deployment-owned secret authority
    /// and then resolves bootstrap coordinates from that authority alone. Environment mode therefore
    /// requires process environment values, not host settings, which is the same contract the
    /// standalone startup lane exercises. The scope restores prior values so it cannot leak.
    /// </summary>
    private sealed class BootstrapAuthorityEnvironmentScope : IDisposable
    {
        private readonly KeyValuePair<string, string?>[] Values =
        [
            new("SECRET_PROVIDER", "Environment"),
            new("INSTANCE_BOOTSTRAP_MODE", "Interactive"),
            new("Database__Provider", "PostgreSql"),
            new("Database__Host", "localhost"),
            new("Database__Database", "guardrail"),
            new("Database__Runtime__Database", "guardrail"),
            new("Database__Migrator__Database", "guardrail"),
            new("Database__Runtime__Username", "postgres"),
            new("Database__Runtime__Password", Convert.ToHexString(RandomNumberGenerator.GetBytes(32))),
        ];

        private readonly List<KeyValuePair<string, string?>> _previous = [];

        public BootstrapAuthorityEnvironmentScope()
        {
            foreach (var value in Values)
            {
                _previous.Add(new(value.Key, Environment.GetEnvironmentVariable(value.Key)));
                Environment.SetEnvironmentVariable(value.Key, value.Value);
            }
        }

        public void Dispose()
        {
            foreach (var previous in _previous)
            {
                Environment.SetEnvironmentVariable(previous.Key, previous.Value);
            }
        }
    }

    private static void ConfigureEarlyHostSettings(IWebHostBuilder builder)
    {
        // The secret authority is selected before application configuration sources are composed,
        // so the selection must be an early host setting rather than an in-memory entry.
        builder.UseSetting("SecretProvider:Provider", "Environment");
        builder.UseSetting("Database:Provider", "PostgreSql");
        builder.UseSetting("Database:Host", "localhost");
        builder.UseSetting("Database:Port", "5432");
        builder.UseSetting("Database:Database", "guardrail");
        builder.UseSetting("Database:Runtime:TlsMode", "Prefer");
        builder.UseSetting("Database:Runtime:TrustServerCertificate", "false");
        builder.UseSetting("EmailDispatchProcessor:Enabled", "false");
        builder.UseSetting("Scheduler:Quartz:Enabled", "false");
        builder.UseSetting("WebhookDeliveryProcessor:Enabled", "false");
        builder.UseSetting("IncomingWebhookProcessing:Enabled", "false");
        builder.UseSetting("HttpsRedirection:Enabled", "false");
        builder.UseSetting("Testing:SkipJwtAuthorityWarmup", "true");
    }
}
