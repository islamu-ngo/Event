using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Npgsql;

namespace Explore.Persistence.Database.ProviderPrimitives;

/// <summary>Classifies insert races separately from EF update/delete token mismatches.</summary>
internal static class LearningWriteConflict
{
    internal static bool IsUniqueViolation(Exception exception) => exception switch
    {
        PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } => true,
        SqliteException { SqliteExtendedErrorCode: 1555 or 2067 } => true,
        SqlException { Number: 2601 or 2627 } => true,
        MySqlException { Number: 1062 } => true,
        DbUpdateException { InnerException: { } inner } => IsUniqueViolation(inner),
        _ => false
    };
}
