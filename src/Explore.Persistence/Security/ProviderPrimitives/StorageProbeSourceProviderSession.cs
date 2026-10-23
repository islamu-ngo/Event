using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Explore.Persistence.Security;

/// <summary>Retains the PostgreSQL source session before observing its enrolled generation.</summary>
internal static class StorageProbeSourceProviderSession
{
    internal static void RequireSupported(ExploreDbContext context)
    {
        if (!context.Database.IsSqlite() && !context.Database.IsNpgsql())
            throw new NotSupportedException("storage_probe_native_binding_custody_unsupported");
    }

    internal static async Task<long?> ObserveAsync(ExploreDbContext context, IConfiguration configuration,
        RelationalConnectionIdentity connection, Action<int> retain, Action<PostgresNativeQueueEnrollment> bind,
        CancellationToken token)
    {
        if (!context.Database.IsNpgsql()) return null;
        await context.Database.OpenConnectionAsync(token);
        retain(connection.PostgresProcessId);
        var enrollment = PostgresNativeQueueEnrollment.Bind(configuration);
        bind(enrollment);
        return await PostgresStorageProbeSourceContract.ObserveAsync(context, enrollment, token);
    }
}
