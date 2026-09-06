// ABOUTME: WebApplicationFactory backed by a real PostgreSQL database with TestAuthHandler authentication.
// Accepts a connection string from Testcontainers and optional configuration overrides per host profile.

using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Constants;
using Explore.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Fixtures;

/// <summary>
/// WebApplicationFactory wired to a real PostgreSQL instance. Uses TestAuthHandler
/// for authentication and StubAuthorizationProvider for authorization.
/// Program.cs skips DB registration in Testing; this factory re-adds it with Npgsql.
/// </summary>
public class PostgreSqlApiWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly Dictionary<string, string?> _additionalConfig;
    private readonly Action<IServiceCollection>? _configureTestServices;

    public PostgreSqlApiWebApplicationFactory(
        string connectionString,
        Dictionary<string, string?>? additionalConfig = null,
        Action<IServiceCollection>? configureTestServices = null)
    {
        _connectionString = connectionString;
        _additionalConfig = additionalConfig ?? [];
        _configureTestServices = configureTestServices;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var (key, value) in _additionalConfig)
            builder.UseSetting(key, value);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var testConfig = new Dictionary<string, string?>
            {
                ["Keycloak:Authority"] = "https://localhost:8443/realms/ISLAMU",
                ["Keycloak:Realm"] = "ISLAMU",
                ["Keycloak:Audience"] = "islamu-event-api",
                ["Keycloak:RequireHttpsMetadata"] = "false",
                ["Keycloak:MetadataAddress"] = "https://localhost:8443/realms/ISLAMU/.well-known/openid-configuration",
                ["S3Settings:Region"] = "us-east-1",
                ["S3Settings:BucketName"] = "test-bucket",
                ["S3Settings:Endpoint"] = "https://localhost:9000",
                ["Deployment:Mode"] = "SingleTenant",
                ["Deployment:DefaultTenantId"] = PlatformDefaults.DefaultTenantId.ToString(),
                ["Testing:UseRealDatabase"] = "true",
                ["Testing:ApplyMigrations"] = "true",
            };

            TestDatabaseConfiguration.AddPostgreSql(testConfig, _connectionString);

            foreach (var kvp in _additionalConfig)
            {
                testConfig[kvp.Key] = kvp.Value;
            }

            config.AddInMemoryCollection(testConfig);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveExploreDbContextRegistrations();

            services.AddPostgreSqlExploreDbContext(_connectionString);

            services.RemoveAll<IDistributedCache>();
            services.AddDistributedMemoryCache();
        });

        builder.ConfigureTestServices(services =>
        {
            TestHostServicePruner.RemoveNoisyHostedServices(services);

            services.AddAuthentication(options =>
            {
                options.DefaultScheme = TestAuthHandler.SchemeName;
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultScheme = TestAuthHandler.SchemeName;
                options.DefaultForbidScheme = TestAuthHandler.SchemeName;
                options.DefaultSignInScheme = TestAuthHandler.SchemeName;
                options.DefaultSignOutScheme = TestAuthHandler.SchemeName;
            });

            services.RemoveAll<IAuthorizationProvider>();
            services.AddSingleton<IAuthorizationProvider>(new StubAuthorizationProvider());

            _configureTestServices?.Invoke(services);
        });
    }
}
