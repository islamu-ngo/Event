using System.Threading.Channels;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Persistence;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Seed;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TUnit.Core.Interfaces;

namespace Event.Api.IntegrationTests.Fixtures;

/// <summary>
/// Abstract base for PostgreSQL-backed test fixtures. Manages container lifecycle,
/// enrolls native purpose roles and provides source-preserving per-test DELETE reset.
/// Subclasses supply host-profile-specific configuration overrides.
/// </summary>
public abstract class PostgreSqlApiFixtureBase : IAsyncInitializer, IAsyncDisposable
{
    private readonly PostgreSqlContainerFixture _database = new();
    private readonly TestPrivacyIdentityFenceKeyProvider _identityFenceKeys = new();

    public PostgreSqlApiWebApplicationFactory Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public TestDatabaseReset DatabaseReset { get; private set; } = null!;
    public string ConnectionString => _database.ConnectionString;

    protected ExploreDbContext CreateSetupDbContext() => _database.CreateDbContext();

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
        var configuration = GetAdditionalConfiguration();
        configuration.TryAdd("PrivacyErasure:Authority:Topology", "EmbeddedSqlite");
        configuration.TryAdd("PrivacyErasureAuthorityEmbedded:Path", _identityFenceKeys.AuthorityPath);
        Factory = new PostgreSqlApiWebApplicationFactory(
            _database,
            configuration,
            services =>
            {
                services.RemoveAll<IPrivacyIdentityFenceKeyProvider>();
                services.AddSingleton<IPrivacyIdentityFenceKeyProvider>(_identityFenceKeys);
                ConfigureAdditionalTestServices(services);
            });

        Client = Factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();

        RecreateHost();
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<EmbeddedPrivacyErasureAuthorityStorage>();
            await storage.EnsureReadyAsync();
            var authorityFactory = scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>>();
            await using var authority = await authorityFactory.CreateDbContextAsync();
            await authority.Database.MigrateAsync();
        }
        DatabaseReset = await TestDatabaseReset.CreateAsync(_database.ConnectionString);
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
        await using var dbContext = _database.CreateDbContext();
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

        await _database.DisposeAsync();
        _identityFenceKeys.Dispose();
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
