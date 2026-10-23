using Explore.Domain.Enums;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class EventReportOperationsQuery
{
    internal static async Task<int?> TryCountNativeAsync(
        ExploreDbContext context, EventReportSyncState state, DateTime? before, CancellationToken cancellationToken)
    {
        if (!context.Database.IsNpgsql()) return null;
        string sql = $"SELECT {PostgresEventReportOperationsContract.FunctionSql(context)}({{0}}, {{1}}) AS \"Value\"";
        var cutoff = new NpgsqlParameter("cutoff", NpgsqlDbType.TimestampTz) { Value = (object?)before ?? DBNull.Value };
        return await context.Database.SqlQueryRaw<int>(sql, (int)state, cutoff).SingleAsync(cancellationToken);
    }
}
