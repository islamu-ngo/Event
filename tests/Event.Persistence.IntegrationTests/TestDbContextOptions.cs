// ABOUTME: Owns EF options isolation for every test-created context and DI factory in this assembly.
// ABOUTME: Keep providers EF-managed for ReplaceService; explicit InMemory roots preserve shared test stores.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests;

internal static class TestDbContextOptions
{
    public static DbContextOptionsBuilder<TContext> Create<TContext>(DbContextOptions<TContext>? options = null)
        where TContext : DbContext
    {
        var builder = options is null
            ? new DbContextOptionsBuilder<TContext>()
            : new DbContextOptionsBuilder<TContext>(options);
        Apply(builder);
        return builder;
    }

    public static DbContextOptionsBuilder Create()
    {
        var builder = new DbContextOptionsBuilder();
        Apply(builder);
        return builder;
    }

    public static void Apply(DbContextOptionsBuilder builder)
    {
        // An uncached build still checks EF's process-wide cache. ALL producers must use this
        // policy before the DbContext constructor; opaque production producers run in child processes.
        builder.EnableServiceProviderCaching(false);
        builder.ConfigureWarnings(warnings => warnings.Throw(CoreEventId.ManyServiceProvidersCreatedWarning));
    }

    public static DbContextOptionsBuilder<TContext> UseTestInMemoryDatabase<TContext>(
        this DbContextOptionsBuilder<TContext> builder,
        string name,
        InMemoryDatabaseRoot? root = null)
        where TContext : DbContext => builder.UseInMemoryDatabase(name, root ?? new InMemoryDatabaseRoot());

    public static DbContextOptionsBuilder UseTestInMemoryDatabase(
        this DbContextOptionsBuilder builder,
        string name,
        InMemoryDatabaseRoot? root = null) => builder.UseInMemoryDatabase(name, root ?? new InMemoryDatabaseRoot());

    public static ServiceProvider BuildIsolatedServiceProvider(
        this IServiceCollection services,
        bool validateScopes = false) => services.BuildIsolatedServiceProvider(
            new ServiceProviderOptions { ValidateScopes = validateScopes });

    public static ServiceProvider BuildIsolatedServiceProvider(
        this IServiceCollection services,
        ServiceProviderOptions options)
    {
        // EF's public configuration pipeline is also used by pooled and non-pooled factories.
        // Register last, for every TContext (not just ExploreDbContext), before options are resolved.
        services.AddSingleton(typeof(IDbContextOptionsConfiguration<>), typeof(IsolatedOptionsConfiguration<>));
        return services.BuildServiceProvider(options);
    }

    private sealed class IsolatedOptionsConfiguration<TContext> : IDbContextOptionsConfiguration<TContext>
        where TContext : DbContext
    {
        public void Configure(IServiceProvider serviceProvider, DbContextOptionsBuilder optionsBuilder) => Apply(optionsBuilder);
    }
}
