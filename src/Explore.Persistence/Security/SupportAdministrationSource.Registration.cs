using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Explore.Persistence.Repositories;
using Explore.Persistence.Identity;

namespace Explore.Persistence.Security;

public static class SupportAdministrationSourceRegistration
{
    public static IServiceCollection AddHumanSupportAdministrationSources(this IServiceCollection services)
    {
        services.AddScoped<ISupportAdministrationSource>(provider =>
        {
            var context = provider.GetRequiredService<ExploreDbContext>();
            var execution = provider.GetRequiredService<ExploreDbContextExecutionLease>();
            return context.Database.IsNpgsql()
                ? new SupportAdministrationPostgresSource(context, execution,
                    NativeEnrollment(provider))
                : new SupportAdministrationSource(context, execution);
        });
        return services;
    }

    /// <summary>
    /// Parent calls after ordinary repository registrations. The original
    /// SupportAccessSessionService keeps its validation, clock, metrics and cache.
    /// Only its exact forwarded owned-session DATA read uses the native capability.
    /// </summary>
    public static IServiceCollection AddHumanSupportAdministrationDataAdapters(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Scoped<ISupportAccessSessionRepository>(provider =>
        {
            var context = provider.GetRequiredService<ExploreDbContext>();
            return context.Database.IsNpgsql()
                ? new SupportAdministrationSessionRepository(context,
                    provider.GetRequiredService<ExploreDbContextExecutionLease>(),
                    () => NativeEnrollment(provider))
                : new SupportAccessSessionRepository(context);
        }));
        return services;
    }

    private static PostgresNativeQueueEnrollment NativeEnrollment(IServiceProvider provider)
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        if (IdentityDatabaseConfiguration.GetTopology(configuration) != IdentityDatabaseTopology.Colocated)
            throw new NotSupportedException("Native support administration requires co-located original sources.");
        return PostgresNativeQueueEnrollment.Bind(configuration);
    }
}
