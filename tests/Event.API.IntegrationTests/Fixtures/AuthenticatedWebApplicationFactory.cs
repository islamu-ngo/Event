using System.Threading.Channels;
using System.Security.Cryptography;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Seed;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Event.Api.IntegrationTests.Fixtures;

/// <summary>
/// WebApplicationFactory that replaces real authentication with TestAuthHandler,
/// and optionally mocks IAuthorizationProvider for authorization integration tests.
/// </summary>
public class AuthenticatedWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("authenticated_api")
        .WithUsername("postgres")
        .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)))
        .Build();

    /// <summary>
    /// When non-null, replaces the real IAuthorizationProvider with this instance.
    /// Set to an allow-all mock for endpoint auth tests, or a selective mock for HATEOAS link tests.
    /// </summary>
    public IAuthorizationProvider? AuthorizationProviderOverride { get; set; }

    /// <summary>Opt in for endpoint scenarios representing an already published directory, not fresh setup.</summary>
    public bool SeedActiveDefaultTenant { get; set; }

    /// <summary>
    /// Additional in-memory configuration applied after the default test host configuration.
    /// </summary>
    public Dictionary<string, string?> AdditionalConfiguration { get; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _database.StartAsync().GetAwaiter().GetResult();
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            var inMemoryConfig = new Dictionary<string, string?>
            {
                {"Keycloak:Authority", "https://auth.example.com"},
                {"Keycloak:Realm", "ISLAMU"},
                {"Keycloak:Audience", "islamu-event-api"},
                {"Keycloak:RequireHttpsMetadata", "false"},
                {"Keycloak:MetadataAddress", "https://auth.example.com/.well-known/openid-configuration"},
                {"Testing:SkipJwtAuthorityWarmup", "true"},
                {"S3Settings:Region", "us-east-1"},
                {"S3Settings:BucketName", "test-bucket"},
                {"S3Settings:Endpoint", "https://s3.example.com"},
                {"Deployment:Mode", "SingleTenant"},
                {"Deployment:DefaultTenantId", PlatformDefaults.DefaultTenantId.ToString()},
                {"PublicBaseUrl", "https://integration.test"},
                {"Instance:OperatorIdentity:OperatorId", "0198e2a4-5340-7f89-8abc-b8bdf43e0ea8"},
                {"Instance:OperatorIdentity:PublicName", "Test Instance Operator"},
                {"Instance:OperatorIdentity:LegalName", "Test Instance Operator ASBL"},
                {"Instance:OperatorIdentity:IsOfficialInstance", "false"},
                {"Instance:OperatorIdentity:OfficialOrigin", "https://instance.example.test"},
                {"Instance:OperatorIdentity:OperatorKindCode", "registered_organization"},
                {"Instance:OperatorIdentity:JurisdictionCountryCode", "BE"},
                {"Instance:OperatorIdentity:RegistrationIdentifier", "BE 0123.456.789"},
                {"Instance:OperatorIdentity:PublicContactEmail", "contact@instance.example.test"},
                {"Instance:OperatorIdentity:WebsiteUrl", "https://instance.example.test"},
                {"Instance:OperatorIdentity:LegalNoticeUrl", "https://instance.example.test/legal"},
                {"Instance:OperatorIdentity:TermsUrl", "https://instance.example.test/terms"},
                {"Instance:OperatorIdentity:PrivacyUrl", "https://instance.example.test/privacy"}
            };

            TestDatabaseConfiguration.AddPostgreSql(inMemoryConfig, _database.GetConnectionString());
            foreach (var pair in AdditionalConfiguration)
            {
                inMemoryConfig[pair.Key] = pair.Value;
            }

            config.AddInMemoryCollection(inMemoryConfig);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveExploreDbContextRegistrations();

            var options = new DbContextOptionsBuilder<ExploreDbContext>();
            ConfigureDatabase(options);
            using (var database = new ExploreDbContext(options.Options))
            {
                database.Database.Migrate();
            }
            services.AddDbContextFactory<ExploreDbContext>(ConfigureDatabase);
            services.AddScoped(provider =>
            {
                var database = provider.GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContext();
                database.ClearTenantFilterBypass();
                database.TenantContext = provider.GetRequiredService<ITenantContext>();
                database.CurrentUserService = provider.GetRequiredService<ICurrentUserService>();
                return database;
            });

            // Override Redis with in-memory distributed cache for tests
            services.RemoveAll<IDistributedCache>();
            services.AddDistributedMemoryCache();

            // Register background seeder to ensure lookup data (roles, etc.) is available in tests
            services.AddHostedService(provider => new SeedingHostedService(
                provider, provider.GetRequiredService<IHostEnvironment>(), SeedActiveDefaultTenant));
        });

        // ConfigureTestServices runs AFTER the app's ConfigureServices,
        // ensuring our auth scheme overrides the real Keycloak JWT registration.
        builder.ConfigureTestServices(services =>
        {
            TestHostServicePruner.RemoveNoisyHostedServices(services);

            // Override ALL default schemes to use TestAuthHandler.
            // Must set DefaultScheme (not just Authenticate/Challenge) because
            // Program.cs sets DefaultScheme = "Bearer" via AddAuthentication("Bearer"),
            // and specific defaults fall back to DefaultScheme if not explicitly set.
            services.AddAuthentication(options =>
            {
                options.DefaultScheme = TestAuthHandler.SchemeName;
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultForbidScheme = TestAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            // Ensure TestAuthHandler is the final default scheme even if
            // Program.cs registers post-configuration for AuthenticationOptions.
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = TestAuthHandler.SchemeName;
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultForbidScheme = TestAuthHandler.SchemeName;
            });

            // Replace IAuthorizationProvider if override provided
            if (AuthorizationProviderOverride is not null)
            {
                services.RemoveAll<IAuthorizationProvider>();
                services.AddScoped(_ => AuthorizationProviderOverride);
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
            // OpenFeature can race its background event executor shutdown in test hosts.
        }
        catch (ObjectDisposedException)
        {
            // Some hosted services can observe disposal while WebApplicationFactory is tearing down.
        }
        catch (NullReferenceException)
        {
            // Test host disposal can race service-provider cleanup after failed startup paths.
        }
        finally
        {
            using var connection = new NpgsqlConnection(_database.GetConnectionString());
            NpgsqlConnection.ClearPool(connection);
            await _database.DisposeAsync();
        }
    }

    private void ConfigureDatabase(DbContextOptionsBuilder options)
    {
        var connection = new NpgsqlConnectionStringBuilder(_database.GetConnectionString());
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Runtime,
            Provider = PrimaryDatabaseProvider.PostgreSql,
            Host = connection.Host,
            Port = connection.Port,
            Database = connection.Database,
            Username = connection.Username,
            Password = connection.Password,
            TlsMode = PrimaryDatabaseTlsMode.Disabled
        });
    }

    private sealed class SeedingHostedService(IServiceProvider serviceProvider, IHostEnvironment environment,
        bool seedActiveDefaultTenant) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            await DatabaseSeeder.SeedAsync(db, environment, cancellationToken: cancellationToken);
            if (seedActiveDefaultTenant && await db.Tenants.FindAsync([PlatformDefaults.DefaultTenantId], cancellationToken) is null)
            {
                db.Tenants.Add(new Event.Api.IntegrationTests.Builders.TenantBuilder()
                    .WithId(PlatformDefaults.DefaultTenantId).Build());
                await db.SaveChangesAsync(cancellationToken);
            }

            // Refresh the lookup cache to ensure it picks up the seeded data (roles, etc.)
            var cache = scope.ServiceProvider.GetService<ILookupDataCache>();
            if (cache != null)
            {
                await cache.RefreshAsync(cancellationToken);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
