using System.Threading.Channels;
using Explore.Domain.Constants;
using Explore.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Fixtures;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Set environment to "Testing"
        builder.UseEnvironment("Testing");
        TestInstanceOperatorIdentityConfiguration.Apply(builder);

        // Force configuration to be available immediately
        builder.ConfigureAppConfiguration((context, config) =>
        {
            var inMemoryConfig = new Dictionary<string, string?>
            {
                {"Database:Provider", "PostgreSql"},
                {"Database:Host", "localhost"},
                {"Database:Port", "5432"},
                {"Database:Database", "explore_db_test"},
                {"Database:Runtime:TlsMode", "Prefer"},
                {"Database:Runtime:TrustServerCertificate", "false"},
                {"Keycloak:Authority", "https://auth.example.com"},
                {"Keycloak:Realm", "ISLAMU"},
                {"Keycloak:Audience", "islamu-event-api"},
                {"Keycloak:RequireHttpsMetadata", "false"},
                {"Keycloak:MetadataAddress", "https://auth.example.com/.well-known/openid-configuration"},
                {"S3Settings:Region", "us-east-1"},
                {"S3Settings:BucketName", "test-bucket"},
                {"S3Settings:Endpoint", "https://s3.example.com"},
                {"Deployment:Mode", "SingleTenant"},
                {"Deployment:DefaultTenantId", PlatformDefaults.DefaultTenantId.ToString()},
            };
            config.AddInMemoryCollection(inMemoryConfig);
        });

        builder.ConfigureServices(services =>
        {
            // Since skipDbContextRegistration=true in "Testing" environment,
            // no Npgsql provider is registered. We simply add InMemory.
            services.AddInMemoryExploreDbContext("InMemoryDbForTesting");

            // Override Redis with in-memory distributed cache for tests
            services.RemoveAll<IDistributedCache>();
            services.AddDistributedMemoryCache();
        });

        builder.ConfigureTestServices(services =>
        {
            TestHostServicePruner.RemoveNoisyHostedServices(services);
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
            // The test host can surface duplicate OpenFeature shutdown on disposal under WebApplicationFactory.
        }
        catch (NullReferenceException)
        {
            // Preserve the existing legacy teardown tolerance used by other integration-test fixtures.
        }
        catch (ObjectDisposedException)
        {
            // OpenFeature can dispose shared shutdown primitives before WebApplicationFactory finishes teardown.
        }
    }
}
