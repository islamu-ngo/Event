using System.Threading.Channels;
using Event.Persistence.IntegrationTests.Fixtures;
using Event.Testing.DataProtection;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Seed;
using Explore.ServiceDefaults.DataProtection;
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

namespace Event.Api.IntegrationTests.Fixtures;

/// <summary>
/// WebApplicationFactory that replaces real authentication with TestAuthHandler,
/// and optionally mocks IAuthorizationProvider for authorization integration tests.
/// </summary>
public class AuthenticatedWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly PostgreSqlContainerFixture _database;
    private readonly bool _ownsDatabase;
    private readonly ExternalDataProtectionTestAuthority _dataProtectionAuthority = new("api");
    private readonly Dictionary<string, string?> _previousEnvironment = [];

    public AuthenticatedWebApplicationFactory() : this(new PostgreSqlContainerFixture(), ownsDatabase: true)
    {
    }

    /// <summary>The caller initializes and owns a supplied native database across host recreations.</summary>
    protected AuthenticatedWebApplicationFactory(PostgreSqlContainerFixture database)
        : this(database, ownsDatabase: false)
    {
    }

    private AuthenticatedWebApplicationFactory(PostgreSqlContainerFixture database, bool ownsDatabase)
    {
        _database = database;
        _ownsDatabase = ownsDatabase;
        ClientOptions.BaseAddress = new Uri("https://integration.test");
    }

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

    protected virtual bool UsesPostgreSqlContainer => true;

    /// <summary>Independent migration-owned arrangement and observation, never a request execution context.</summary>
    public ExploreDbContext CreateMigratorContext()
    {
        if (!UsesPostgreSqlContainer)
            throw new InvalidOperationException("This fixture does not own a PostgreSQL migration database.");
        var options = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(
            options, _database.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var context = new ExploreDbContext(options.Options);
        context.EnableTenantFilterBypass("API integration test arrangement and observation.");
        context.BindTenantLease();
        return context;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var nativeConfiguration = new Dictionary<string, string?>();
        if (UsesPostgreSqlContainer)
        {
            if (_ownsDatabase)
                _database.InitializeAsync().GetAwaiter().GetResult();
            var runtime = _database.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime);
            TestDatabaseConfiguration.AddPostgreSql(nativeConfiguration, new NpgsqlConnectionStringBuilder
            {
                Host = runtime.Host,
                Port = runtime.Port ?? 5432,
                Database = runtime.Database,
                Username = runtime.Username,
                Password = runtime.Password,
                SslMode = SslMode.Disable
            }.ConnectionString);
            nativeConfiguration["PrivacyErasure:Authority:Topology"] = "CoLocated";
            foreach (var role in Enum.GetValues<PrimaryDatabaseRole>())
            {
                var account = _database.GetNativeQueueOptions(role);
                nativeConfiguration[$"Database:{role}:Database"] = account.Database;
                nativeConfiguration[$"Database:{role}:Username"] = account.Username;
                nativeConfiguration[$"Database:{role}:Password"] = account.Password;
                nativeConfiguration[$"Database:{role}:TlsMode"] = account.TlsMode.ToString();
            }
            foreach (var pair in nativeConfiguration)
            {
                builder.UseSetting(pair.Key, pair.Value);
                SetEnvironment(pair.Key.Replace(":", "__", StringComparison.Ordinal), pair.Value);
            }
            SetEnvironment("SECRET_PROVIDER", "Environment");
            SetEnvironment("SecretProvider__Provider", "Environment");
        }
        foreach (var pair in AdditionalConfiguration)
        {
            builder.UseSetting(pair.Key, pair.Value);
            SetEnvironment(pair.Key.Replace(":", "__", StringComparison.Ordinal), pair.Value);
        }
        builder.UseEnvironment("Testing");
        ClientOptions.BaseAddress = new Uri(
            AdditionalConfiguration.GetValueOrDefault("EffectiveHostAuthority:CanonicalOrigin")
            ?? AdditionalConfiguration.GetValueOrDefault("PublicBaseUrl")
            ?? "https://integration.test");

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
                {"EffectiveHostAuthority:CanonicalOrigin", ClientOptions.BaseAddress.GetLeftPart(UriPartial.Authority)},
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

            foreach (var pair in nativeConfiguration)
                inMemoryConfig[pair.Key] = pair.Value;
            foreach (var pair in AdditionalConfiguration)
            {
                inMemoryConfig[pair.Key] = pair.Value;
            }

            config.AddInMemoryCollection(inMemoryConfig);
        });

        builder.ConfigureServices(services =>
        {
            if (UsesPostgreSqlContainer)
            {
                services.RemoveExploreDbContextRegistrations();

                services.AddDbContextFactory<ExploreDbContext>(ConfigureDatabase);
                services.AddScoped(provider =>
                {
                    if (provider.GetRequiredService<ExploreDbContextExecutionLease>().Context is { } selected)
                        return selected;
                    var database = provider.GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContext();
                    database.ClearTenantFilterBypass();
                    database.TenantContext = provider.GetRequiredService<ITenantContext>();
                    database.CurrentUserService = provider.GetRequiredService<ICurrentUserService>();
                    database.BindTenantLease();
                    return database;
                });
            }

            // Override Redis with in-memory distributed cache for tests
            services.RemoveAll<IDistributedCache>();
            services.AddDistributedMemoryCache();

            // Register background seeder to ensure lookup data (roles, etc.) is available in tests
            services.AddHostedService(provider => new SeedingHostedService(
                provider, provider.GetRequiredService<IHostEnvironment>(), SeedActiveDefaultTenant,
                UsesPostgreSqlContainer ? _database : null));
        });

        // ConfigureTestServices runs AFTER the app's ConfigureServices,
        // ensuring our auth scheme overrides the real Keycloak JWT registration.
        builder.ConfigureTestServices(services =>
        {
            TestHostServicePruner.RemoveNoisyHostedServices(services);
            services.RemoveAll<IExternalDataProtectionSource>();
            services.AddSingleton<IExternalDataProtectionSource>(_dataProtectionAuthority);

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
            foreach (var pair in _previousEnvironment)
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            if (_ownsDatabase)
                await _database.DisposeAsync();
        }
    }

    private void SetEnvironment(string name, string? value)
    {
        _previousEnvironment.TryAdd(name, Environment.GetEnvironmentVariable(name));
        Environment.SetEnvironmentVariable(name, value);
    }

    private void ConfigureDatabase(DbContextOptionsBuilder options)
    {
        PrimaryDatabaseProviderComposition.ConfigureApplication(options,
            _database.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime));
        options.AddInterceptors(Explore.Persistence.Security.PostgresTenantSessionInterceptor.RuntimeInstance);
    }

    private sealed class SeedingHostedService(IServiceProvider serviceProvider, IHostEnvironment environment,
        bool seedActiveDefaultTenant, PostgreSqlContainerFixture? nativeDatabase) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = serviceProvider.CreateScope();
            await using var db = nativeDatabase is null
                ? scope.ServiceProvider.GetRequiredService<ExploreDbContext>()
                : nativeDatabase.CreateDbContext();
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
