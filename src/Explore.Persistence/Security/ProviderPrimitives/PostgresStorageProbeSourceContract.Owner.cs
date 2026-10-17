using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

public static partial class PostgresStorageProbeSourceContract
{
    internal static Task<long> ObserveAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken token) => ReadAsync(context, enrollment, false, token);

    internal static Task<long> AcquireAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken token) => ReadAsync(context, enrollment, true, token);

    private static async Task<long> ReadAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        bool acquire, CancellationToken token)
    {
        RequireConnection(context);
        var connection = context.Database.GetDbConnection();
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        if (acquire && (transaction is null || !context.HumanCommitAttemptOwnsTransaction
            || transaction.IsolationLevel != IsolationLevel.Serializable || !ReferenceEquals(transaction.Connection, connection)))
            throw new UnauthorizedAccessException("Storage probe requires its original non-replaying source attempt.");
        await RequireReadyAsync(context, enrollment, token);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        command.CommandText = $"SELECT {FunctionSql(context, acquire ? "acquire" : "observe")}(@tenant)";
        command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid)
        {
            Value = (object?)context.TenantFilterTenantId ?? DBNull.Value
        });
        var value = await command.ExecuteScalarAsync(token);
        if (value is not long generation || generation < 0)
            throw new UnauthorizedAccessException("Storage probe native source generation is absent.");
        RequireConnection(context);
        if (!ReferenceEquals(connection, context.Database.GetDbConnection())
            || !ReferenceEquals(transaction, context.Database.CurrentTransaction?.GetDbTransaction()))
            throw new UnauthorizedAccessException("Storage probe native source owner changed.");
        return generation;
    }

    private static void RequireConnection(ExploreDbContext context)
    {
        context.ValidateTenantLease();
        if (!context.Database.IsNpgsql() || context.Database.GetDbConnection().State != ConnectionState.Open
            || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner)
            throw new UnauthorizedAccessException("Storage probe requires its retained ordinary open connection.");
    }

}
