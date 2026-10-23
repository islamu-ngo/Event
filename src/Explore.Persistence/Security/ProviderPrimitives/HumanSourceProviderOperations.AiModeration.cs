using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

internal static partial class HumanSourceProviderOperations
{
    internal static Task LockAsync(ExploreDbContext context, HumanAiModerationProposalTargetQueries queries,
        DbTransaction transaction, CancellationToken token) =>
        context.Database.IsNpgsql() ? Task.CompletedTask : queries.LockPortableAsync(transaction, token);
}
