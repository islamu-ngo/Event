using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Security;

/// <summary>Selects the enrolled native ordinary-settings observation without acquiring the attempt.</summary>
internal static class OrdinarySettingsSourceProviderQueries
{
    internal static void RequireSupported(ExploreDbContext context)
    {
        if (!context.Database.IsSqlite() && !context.Database.IsNpgsql())
            throw new NotSupportedException("ordinary_settings_native_source_custody_not_supported");
    }

    internal static async Task<PostgresOrdinarySettingsContract.IObservation?> ReadObservationAsync(
        ExploreDbContext context, IServiceProvider services, ExploreDbContextExecutionLease selection,
        OrdinarySettingsMutation mutation, HumanCommitSubject subject, TimeProvider clock, CancellationToken token) =>
        context.Database.IsNpgsql()
            ? await PostgresOrdinarySettingsContract.ReadAsync(context,
                services.GetRequiredService<PostgresNativeQueueEnrollment>(), selection, mutation, subject, clock, token)
            : null;
}
