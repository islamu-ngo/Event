// ABOUTME: Abstract base fixture managing PostgreSQL container lifecycle, migrations, seeding, and Respawn reset.
// ABOUTME: Subclassed by RealRuntimeApiFixture and StressApiFixture with profile-specific configuration.

using System.Threading.Channels;
using System.Security.Cryptography;
using Explore.Persistence;
using Explore.Persistence.Seed;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

namespace Event.Api.IntegrationTests.Fixtures;

/// <summary>
/// Abstract base for PostgreSQL-backed test fixtures. Manages container lifecycle,
/// runs migrations and lookup seeding, and provides per-test Respawn reset.
/// Subclasses supply host-profile-specific configuration overrides.
/// </summary>
public abstract class PostgreSqlApiFixtureBase : IAsyncInitializer, IAsyncDisposable
{
    private readonly PostgreSqlContainer _container;

    public PostgreSqlApiWebApplicationFactory Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public TestDatabaseReset DatabaseReset { get; private set; } = null!;
    public string ConnectionString => _container.GetConnectionString();

    protected PostgreSqlApiFixtureBase()
    {
        _container = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("explore_db_test")
            .WithUsername("postgres")
            .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)))
            .Build();
    }

    /// <summary>
    /// Returns additional configuration entries specific to the host profile.
    /// Merged into the WebApplicationFactory's in-memory configuration.
    /// </summary>
    protected abstract Dictionary<string, string?> GetAdditionalConfiguration();

    protected virtual void ConfigureAdditionalTestServices(IServiceCollection services)
    {
    }

    private void RecreateHost()
    {
        Factory = new PostgreSqlApiWebApplicationFactory(
            _container.GetConnectionString(),
            GetAdditionalConfiguration(),
            ConfigureAdditionalTestServices);

        Client = Factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var options = new DbContextOptionsBuilder<ExploreDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var dbContext = new ExploreDbContext(options);
        await dbContext.Database.MigrateAsync();
        await LookupTableSeeder.SeedAsync(dbContext);

        RecreateHost();
        DatabaseReset = await TestDatabaseReset.CreateAsync(_container.GetConnectionString());
    }

    /// <summary>
    /// Resets the database to post-migration, post-lookup-seed state.
    /// Call at the start of each test for deterministic isolation.
    /// </summary>
    public async Task ResetDatabaseAsync()
    {
        await DatabaseReset.ResetAsync();

        Client?.Dispose();
        if (Factory is not null)
        {
            await DisposeFactoryAsync();
        }

        RecreateHost();
        var factory = Factory ?? throw new InvalidOperationException("PostgreSQL API test host was not recreated.");

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await LookupTableSeeder.SeedAsync(dbContext);

        var outputCacheStore = scope.ServiceProvider.GetRequiredService<IOutputCacheStore>();

        // Defensive cleanup for output-cache entries that also exist inside the recreated host.
        await outputCacheStore.EvictByTagAsync("list-data", default);
        await outputCacheStore.EvictByTagAsync("detail-data", default);
    }

    /// <summary>
    /// Creates an HTTP request with standard authenticated user claims.
    /// </summary>
    public HttpRequestMessage CreateAuthenticatedRequest(
        HttpMethod method,
        string url,
        Guid? userId = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(
            "X-Test-Auth",
            TestAuthHandler.CreateAuthHeaderValue(userId ?? Guid.NewGuid(), "Test User"));
        return request;
    }

    /// <summary>
    /// Creates an HTTP request with instance admin claims.
    /// </summary>
    public HttpRequestMessage CreateInstanceAdminRequest(
        HttpMethod method,
        string url,
        Guid? userId = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(
            "X-Test-Auth",
            TestAuthHandler.CreateInstanceAdminHeaderValue(userId ?? Guid.NewGuid()));
        return request;
    }

    /// <summary>
    /// Creates an HTTP request with tenant admin claims for a specific tenant.
    /// </summary>
    public HttpRequestMessage CreateTenantAdminRequest(
        HttpMethod method,
        string url,
        Guid tenantId,
        Guid? userId = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(
            "X-Test-Auth",
            TestAuthHandler.CreateTenantAdminHeaderValue(userId ?? Guid.NewGuid(), tenantId));
        return request;
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();

        if (Factory is not null)
        {
            await DisposeFactoryAsync();
        }

        await _container.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private async ValueTask DisposeFactoryAsync()
    {
        var factory = Factory;
        if (factory is null)
        {
            return;
        }

        try
        {
            await factory.DisposeAsync();
        }
        catch (ChannelClosedException)
        {
            // OpenFeature can race WebApplicationFactory shutdown in tests after the host has already stopped.
        }
        catch (NullReferenceException)
        {
            // Some hosted providers are already torn down when TestHost disposes repeated host instances.
        }
        catch (ObjectDisposedException)
        {
            // Disposal is idempotent for the test fixture; repeated shutdown signals are safe to ignore.
        }
    }
}
