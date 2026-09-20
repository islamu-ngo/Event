using Explore.Application.Contracts.Infrastructure;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Fixtures;

/// <summary>Independent API hosts and external writers sharing one disposable authoritative SQLite store.</summary>
internal sealed class AdminAuthorityFreshnessFactory(string databasePath, IInterceptor? interceptor = null)
    : AuthenticatedWebApplicationFactory
{
    public static async Task<AdminAuthorityFreshnessFactory> CreateAsync(IInterceptor? interceptor = null)
    {
        var factory = new AdminAuthorityFreshnessFactory(
            Path.Combine(Path.GetTempPath(), $"admin-authority-{Guid.CreateVersion7():N}.db"), interceptor);
        factory.ConfigureAuthority();
        try
        {
            await using var db = factory.ExternalDatabase();
            await db.Database.EnsureCreatedAsync();
            await SqliteDatabaseInitializer.InitializeAsync(db, default);
            return factory;
        }
        catch
        {
            await factory.DisposeAsync();
            factory.DeleteDatabase();
            throw;
        }
    }

    public AdminAuthorityFreshnessFactory IndependentHost()
    {
        var factory = new AdminAuthorityFreshnessFactory(databasePath);
        factory.ConfigureAuthority();
        return factory;
    }

    private void ConfigureAuthority()
    {
        AdditionalConfiguration["Authorization:Provider"] = "local";
        AdditionalConfiguration["EmailDispatchProcessor:Enabled"] = "false";
        AdditionalConfiguration["EmailDispatchRabbitMq:Enabled"] = "false";
        AdditionalConfiguration["OutboxProcessor:Enabled"] = "false";
    }

    public ExploreDbContext ExternalDatabase()
    {
        var options = new DbContextOptionsBuilder<ExploreDbContext>();
        ConfigureDatabase(options);
        return new ExploreDbContext(options.Options);
    }

    private void ConfigureDatabase(DbContextOptionsBuilder options)
    {
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Runtime,
            Provider = PrimaryDatabaseProvider.Sqlite,
            Database = databasePath
        });
        options.UseSnakeCaseNamingConvention();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveExploreDbContextRegistrations();
            services.AddDbContextFactory<ExploreDbContext>(options =>
            {
                ConfigureDatabase(options);
                if (interceptor is not null) options.AddInterceptors(interceptor);
            });
            services.AddScoped(provider =>
            {
                var db = provider.GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContext();
                db.TenantContext = provider.GetRequiredService<ITenantContext>();
                db.CurrentUserService = provider.GetRequiredService<ICurrentUserService>();
                return db;
            });
        });
    }

    // The store is owned by the test, not either host; delete only after both hosts and readers dispose.
    public void DeleteDatabase()
    {
        File.Delete(databasePath);
        File.Delete(databasePath + "-wal");
        File.Delete(databasePath + "-shm");
    }
}
