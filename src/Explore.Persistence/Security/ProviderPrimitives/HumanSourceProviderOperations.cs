using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

/// <summary>
/// Selects the native enrollment and physical source-lock operations. Callers retain
/// source ordering, authority checks and ownership of the original transaction.
/// </summary>
internal static partial class HumanSourceProviderOperations
{
    internal static void RequireEnrollment(ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment)
    {
        if (context.Database.IsNpgsql())
        {
            ArgumentNullException.ThrowIfNull(enrollment);
            enrollment.Validate();
        }
    }

    internal static Task AcquirePolicySourcesAsync(ExploreDbContext context, PolicySourceSetProviderLock sources,
        DbTransaction transaction, CancellationToken token) =>
        context.Database.IsNpgsql()
            ? sources.AcquirePostgresAsync(transaction, token)
            : sources.AcquirePortableAsync(transaction, token);

    internal static Task ValidatePolicySourcesAsync(ExploreDbContext context, PolicySourceSetProviderLock sources,
        DbTransaction transaction, CancellationToken token) =>
        context.Database.IsNpgsql() ? sources.AcquirePostgresAsync(transaction, token) : Task.CompletedTask;
}
