using Explore.Application.Contracts.Infrastructure;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Fixtures;

internal static class TestDbContextServiceCollectionExtensions
{
    public static IServiceCollection AddInMemoryExploreDbContext(
        this IServiceCollection services,
        string databaseName)
    {
        services.AddDbContextFactory<ExploreDbContext>(options =>
        {
            options.UseInMemoryDatabase(databaseName);
            options.ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning));
        });

        services.AddScoped(sp =>
        {
            var factory = sp.GetRequiredService<IDbContextFactory<ExploreDbContext>>();
            var context = factory.CreateDbContext();

            context.ClearTenantFilterBypass();
            context.TenantContext = sp.GetService<ITenantContext>();
            context.CurrentUserService = sp.GetService<ICurrentUserService>();

            return context;
        });

        return services;
    }

    public static IServiceCollection AddPostgreSqlExploreDbContext(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContextFactory<ExploreDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
            options.UseSnakeCaseNamingConvention();
            options.ConfigureWarnings(warnings =>
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        });

        services.AddScoped(sp =>
        {
            var factory = sp.GetRequiredService<IDbContextFactory<ExploreDbContext>>();
            var context = factory.CreateDbContext();

            context.ClearTenantFilterBypass();
            context.TenantContext = sp.GetService<ITenantContext>();
            context.CurrentUserService = sp.GetService<ICurrentUserService>();

            return context;
        });

        return services;
    }

    public static IServiceCollection RemoveExploreDbContextRegistrations(this IServiceCollection services)
    {
        var descriptors = services
            .Where(IsExploreDbContextRegistration)
            .ToList();

        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);
        }

        return services;
    }

    private static bool IsExploreDbContextRegistration(ServiceDescriptor descriptor)
    {
        return IsExploreDbContextType(descriptor.ServiceType)
            || IsExploreDbContextType(descriptor.ImplementationType);
    }

    private static bool IsExploreDbContextType(Type? type)
    {
        if (type is null)
        {
            return false;
        }

        if (type == typeof(ExploreDbContext)
            || type == typeof(DbContextOptions)
            || type == typeof(DbContextOptions<ExploreDbContext>)
            || type == typeof(IDbContextFactory<ExploreDbContext>))
        {
            return true;
        }

        return type.IsGenericType
            && type.GetGenericArguments().Contains(typeof(ExploreDbContext))
            && type.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true;
    }
}
