using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

public static partial class PostgresOperatorIdentityEditorContract
{
    internal static DateTime NormalizeEffectTimestamp(ExploreDbContext context, DateTime timestamp) =>
        context.Database.IsNpgsql()
            ? new DateTime(timestamp.Ticks - timestamp.Ticks % 10, timestamp.Kind)
            : timestamp;

    internal static async Task<(PostgresNativeQueueEnrollment Enrollment, int Backend)?> TryPrepareAsync(
        ExploreDbContext context, RelationalConnectionIdentity connection,
        Func<PostgresNativeQueueEnrollment> enrollment, CancellationToken cancellationToken)
    {
        if (!context.Database.IsNpgsql()) return null;
        var selected = enrollment();
        await context.Database.OpenConnectionAsync(cancellationToken);
        int backend = connection.PostgresProcessId;
        await RequireReadyAsync(context, selected, cancellationToken);
        return (selected, backend);
    }

    internal static async Task<string> RepresentAuditPayloadAsync(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, string payload,
        CancellationToken cancellationToken)
    {
        await RequireReadyAsync(context, enrollment, cancellationToken);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        command.CommandText = "SELECT CAST(CAST(@payload AS pg_catalog.jsonb) AS pg_catalog.text)";
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Text) { Value = payload });
        return (string)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new UnauthorizedAccessException("The native operator audit representation is missing."));
    }
}
