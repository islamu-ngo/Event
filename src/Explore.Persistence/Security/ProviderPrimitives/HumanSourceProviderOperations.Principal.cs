using System.Data.Common;
using Explore.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

internal static partial class HumanSourceProviderOperations
{
    internal static Task LockAsync(ExploreDbContext context, HumanPrincipalSourceQueries queries,
        DbTransaction transaction, CancellationToken token) =>
        context.Database.IsNpgsql() ? Task.CompletedTask : queries.LockPortableAsync(transaction, token);

    internal static void RetainNativeTenantOwner(ExploreDbContext context, HumanCommitSubject subject, Guid tenant)
    {
        if (context.Database.IsNpgsql())
            context.RetainNativeHumanTenantOwner(subject, tenant);
    }
}
