using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Explore.Persistence.Security;

internal static partial class HumanSourceProviderOperations
{
    internal static PostgresNativeQueueEnrollment? BindEnrollment(
        ExploreDbContext context, Func<IConfiguration> configuration) =>
        context.Database.IsNpgsql() ? PostgresNativeQueueEnrollment.Bind(configuration()) : null;

    internal static void RequirePostgresOrSqlite(ExploreDbContext context, string refusal)
    {
        if (!context.Database.IsNpgsql() && !context.Database.IsSqlite())
            throw new NotSupportedException(refusal);
    }



    internal static Task LockAsync(ExploreDbContext context, HumanEventProviderSourceQueries queries,
        DbTransaction transaction, CancellationToken token) =>
        context.Database.IsNpgsql() ? Task.CompletedTask : queries.RequirePortableAsync(transaction, token);

}
