// ABOUTME: PostgreSQL container fixture for projection and event-location repository tests.
// ABOUTME: Uses the current EF model plus canonical lookup seeding without migration-history coupling.

using Explore.Application.Contracts.Infrastructure;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Schema;
using Explore.Persistence.Seed;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

namespace Event.Persistence.IntegrationTests.Fixtures;

/// <summary>
/// Lightweight container fixture scoped to projection integration tests. It does not rely on
/// migration files, constructs schema directly from the current model via
/// <see cref="DatabaseFacade.EnsureCreatedAsync"/>, and repairs canonical lookup rows before use.
/// </summary>
public class ProjectionTestContainerFixture : IAsyncInitializer, IAsyncDisposable
{
    private readonly PostgreSqlContainer _container;

    public ProjectionTestContainerFixture()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:18-alpine")
            .WithDatabase("explore_db_projection")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
    }

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var context = CreateDbContextInternal();
        await context.Database.EnsureCreatedAsync();
        await LookupTableSeeder.SeedAsync(context);
    }

    public async ValueTask DisposeAsync()
    {
        await _container.StopAsync();
        await _container.DisposeAsync();
    }

    public ExploreDbContext CreateDbContext()
    {
        var context = CreateDbContextInternal();
        context.EnableTenantFilterBypass("Projection integration test system context.");
        return context;
    }

    public ExploreDbContext CreateDbContext(ITenantContext tenantContext)
    {
        var context = CreateDbContextInternal();
        context.TenantContext = tenantContext;
        return context;
    }

    private ExploreDbContext CreateDbContextInternal()
    {
        var connection = new NpgsqlConnectionStringBuilder(_container.GetConnectionString());
        var database = new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Runtime,
            Provider = PrimaryDatabaseProvider.PostgreSql,
            Host = connection.Host,
            Port = connection.Port,
            Database = connection.Database,
            Schema = RelationalModelNamespace.DefaultSchema,
            Username = connection.Username,
            Password = connection.Password,
            TlsMode = PrimaryDatabaseTlsMode.Disabled,
        };
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, database);
        return new ExploreDbContext(builder.Options);
    }
}
