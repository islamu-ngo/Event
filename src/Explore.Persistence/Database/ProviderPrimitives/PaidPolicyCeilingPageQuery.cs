using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class PaidPolicyCeilingPageQuery
{
    internal static Task<string[]>? ReadNativeAsync(
        ExploreDbContext context, int offset, int limit, CancellationToken token)
    {
        if (!context.Database.IsNpgsql())
            return null;
        string query = $"SELECT page AS \"Value\" FROM {PostgresPaidPolicyCeilingContract.FunctionSql(context)}({{0}}, {{1}}) page";
        return context.Database.SqlQueryRaw<string>(query, offset, limit).ToArrayAsync(token);
    }
}
