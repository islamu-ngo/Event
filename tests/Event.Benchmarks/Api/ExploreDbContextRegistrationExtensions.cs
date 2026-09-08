using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Benchmarks.Api;

internal static class ExploreDbContextRegistrationExtensions
{
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
