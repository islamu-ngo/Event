namespace Explore.Persistence.Database.ProviderPrimitives;

/// <summary>
/// Compares SQLite's mapped DateTimeOffset text at full tick precision.
/// Whole seconds include the stored offset; fractional ticks are padded separately
/// so SQLite's millisecond date-function rounding cannot extend a support horizon.
/// </summary>
internal static class SqliteSupportAccessClockSql
{
    internal static string Ticks(string column) =>
        $"(CAST(strftime('%s',substr({column},1,19)||substr({column},-6)) AS INTEGER)*10000000"
        + $"+CAST(substr((CASE WHEN substr({column},20,1)='.' THEN substr({column},21,length({column})-26)"
        + " ELSE '' END)||'0000000',1,7) AS INTEGER))";

    internal static long Ticks(DateTimeOffset instant) =>
        instant.UtcDateTime.Ticks - DateTime.UnixEpoch.Ticks;
}
