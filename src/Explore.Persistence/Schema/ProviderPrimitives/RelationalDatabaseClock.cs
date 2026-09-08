using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Schema.ProviderPrimitives;

internal static class RelationalDatabaseClock
{
    public static async Task<DateTime> GetUtcNowAsync(
        ExploreDbContext dbContext,
        CancellationToken cancellationToken)
    {
        string sql = SelectUtcNowSql(dbContext.Database.ProviderName);
        DateTime value = await dbContext.Database
            .SqlQueryRaw<DateTime>(sql)
            .SingleAsync(cancellationToken);
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    internal static string SelectUtcNowSql(string? providerName) => providerName switch
    {
        RelationalNamedLock.PostgreSqlProvider => "SELECT clock_timestamp() AS \"Value\"",
        RelationalNamedLock.SqliteProvider => "SELECT CURRENT_TIMESTAMP AS \"Value\"",
        RelationalNamedLock.SqlServerProvider => "SELECT SYSUTCDATETIME() AS [Value]",
        RelationalNamedLock.MySqlProvider => "SELECT UTC_TIMESTAMP(6) AS `Value`",
        _ => throw new InvalidOperationException(
            $"Unsupported relational database clock provider '{providerName ?? "<unknown>"}'.")
    };
}
