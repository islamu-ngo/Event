using System.Security.Claims;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Fixtures;

public sealed class NativeEventAspectsFactory : AuthenticatedWebApplicationFactory
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"native-event-aspects-{Guid.CreateVersion7():N}.db");

    public NativeEventAspectsFactory()
    {
        AdditionalConfiguration["Authorization:Provider"] = "local";
        AdditionalConfiguration["Testing:DisableDeploymentModeCache"] = "true";
        AdditionalConfiguration["Keycloak:Authority"] = "";
        AdditionalConfiguration["Keycloak:AuthorizationUrl"] = "";
    }

    public IServiceScope Scope(Guid? userId = null, Guid? tenantId = null)
    {
        var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(tenantId ?? PlatformDefaults.DefaultTenantId);
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = userId is { } id
                ? new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", id.ToString())], "Test"))
                : new ClaimsPrincipal(new ClaimsIdentity())
        };
        return scope;
    }

    public HttpClient Client(Guid userId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(userId));
        return client;
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
        });
    }

    private void ConfigureDatabase(DbContextOptionsBuilder options)
    {
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Runtime,
            Provider = PrimaryDatabaseProvider.Sqlite,
            Database = _path
        });
        options.UseSnakeCaseNamingConvention();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        var options = new DbContextOptionsBuilder<ExploreDbContext>();
        ConfigureDatabase(options);
        await using var db = new ExploreDbContext(options.Options);
        SqliteConnection.ClearPool((SqliteConnection)db.Database.GetDbConnection());
        File.Delete(_path);
        File.Delete(_path + "-wal");
        File.Delete(_path + "-shm");
    }
}
