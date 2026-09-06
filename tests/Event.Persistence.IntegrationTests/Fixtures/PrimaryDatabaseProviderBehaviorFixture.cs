// ABOUTME: Structured real-provider fixture for the shared primary database behavior contract.
// ABOUTME: Reuses production provider composition without accepting raw connection strings from operators.

using Explore.Application.Contracts.Infrastructure;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Seed;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Fixtures;

internal sealed class PrimaryDatabaseProviderBehaviorFixture
{
    private readonly PrimaryDatabaseConnectionOptions _databaseOptions;

    private PrimaryDatabaseProviderBehaviorFixture(PrimaryDatabaseConnectionOptions databaseOptions)
    {
        _databaseOptions = databaseOptions;
    }

    public PrimaryDatabaseProvider Provider => _databaseOptions.Provider;

    public static PrimaryDatabaseProviderBehaviorFixture Create()
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        return new PrimaryDatabaseProviderBehaviorFixture(
            PrimaryDatabaseConfiguration.BindRuntime(configuration));
    }

    public static PrimaryDatabaseProviderBehaviorFixture Create(
        PrimaryDatabaseConnectionOptions databaseOptions)
    {
        ArgumentNullException.ThrowIfNull(databaseOptions);
        return new PrimaryDatabaseProviderBehaviorFixture(databaseOptions);
    }

    public ExploreDbContext CreateSystemContext(params IInterceptor[] interceptors)
    {
        var context = CreateContext(interceptors);
        context.EnableTenantFilterBypass("Shared primary database provider behavior contract.");
        return context;
    }

    public ExploreDbContext CreateTenantContext(Guid? tenantId, params IInterceptor[] interceptors)
    {
        var context = CreateContext(interceptors);
        context.TenantContext = tenantId.HasValue ? new TestTenantContext(tenantId.Value) : null;
        return context;
    }

    public async Task PrepareAsync()
    {
        await using var context = CreateSystemContext();
        if (!await context.Database.CanConnectAsync())
        {
            throw new InvalidOperationException($"The configured {Provider} behavior-contract database is unavailable.");
        }

        await LookupTableSeeder.SeedAsync(context);
    }

    public ServiceProvider BuildDataProtectionProvider(string applicationName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<DataProtectionKeyContext>(options =>
        {
            PrimaryDatabaseProviderComposition.ConfigureDataProtection(options, _databaseOptions);
        });
        services
            .AddDataProtection()
            .SetApplicationName(applicationName)
            .PersistKeysToDbContext<DataProtectionKeyContext>();

        return services.BuildIsolatedServiceProvider(validateScopes: true);
    }

    public DataProtectionKeyContext CreateDataProtectionContext()
    {
        var builder = TestDbContextOptions.Create<DataProtectionKeyContext>();
        PrimaryDatabaseProviderComposition.ConfigureDataProtection(builder, _databaseOptions);
        return new DataProtectionKeyContext(builder.Options);
    }

    private ExploreDbContext CreateContext(params IInterceptor[] interceptors)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, _databaseOptions);
        if (interceptors.Length > 0)
        {
            builder.AddInterceptors(interceptors);
        }
        return new ExploreDbContext(builder.Options);
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}

internal sealed class RequiresStructuredPrimaryDatabaseAttribute()
    : SkipAttribute("Structured Database provider inputs are unavailable; run this contract through its explicit runtime lane.")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext _)
        => Task.FromResult(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("Database__Provider")));
}
