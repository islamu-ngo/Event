using Explore.Application.Contracts.Infrastructure;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Fixtures;

public sealed class NativeEventSeriesFactory : AuthenticatedWebApplicationFactory
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"native-series-{Guid.CreateVersion7():N}.db");
    public IInterceptor? DatabaseInterceptor { get; init; }
    public TimeProvider Clock { get; init; } = TimeProvider.System;

    public NativeEventSeriesFactory()
    {
        AdditionalConfiguration["Authorization:Provider"] = "local";
        AdditionalConfiguration["Testing:DisableDeploymentModeCache"] = "true";
        AdditionalConfiguration["Keycloak:Authority"] = "";
        AdditionalConfiguration["Keycloak:AuthorizationUrl"] = "";
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveExploreDbContextRegistrations();
            var options = new DbContextOptionsBuilder<ExploreDbContext>();
            ConfigureDatabase(options);
            using (var db = new ExploreDbContext(options.Options))
                db.Database.EnsureCreated();
            services.AddDbContextFactory<ExploreDbContext>(ConfigureDatabase);
            services.AddScoped(provider =>
            {
                var db = provider.GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContext();
                db.TenantContext = provider.GetRequiredService<ITenantContext>();
                db.CurrentUserService = provider.GetRequiredService<ICurrentUserService>();
                return db;
            });
            services.RemoveAll<TimeProvider>();
            services.AddSingleton(Clock);
        });
    }

    private void ConfigureDatabase(DbContextOptionsBuilder options)
    {
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Runtime, Provider = PrimaryDatabaseProvider.Sqlite, Database = _databasePath
        });
        options.UseSnakeCaseNamingConvention();
        if (DatabaseInterceptor is not null)
            options.AddInterceptors(DatabaseInterceptor);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        var options = new DbContextOptionsBuilder<ExploreDbContext>();
        ConfigureDatabase(options);
        await using var db = new ExploreDbContext(options.Options);
        SqliteConnection.ClearPool((SqliteConnection)db.Database.GetDbConnection());
        File.Delete(_databasePath);
        File.Delete(_databasePath + "-wal");
        File.Delete(_databasePath + "-shm");
    }
}
