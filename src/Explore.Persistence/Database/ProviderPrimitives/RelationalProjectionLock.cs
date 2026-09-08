using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class RelationalProjectionLock
{
    public static async Task<bool> TryAcquireAsync(
        ExploreDbContext dbContext,
        int projectionLockKey,
        Guid tenantId,
        bool exclusive,
        CancellationToken cancellationToken)
    {
        string providerName = dbContext.Database.ProviderName
            ?? throw new InvalidOperationException("Projection locks require a configured database provider.");
        if (providerName == RelationalNamedLock.SqliteProvider)
        {
            return TryAcquireSqlite(
                dbContext,
                projectionLockKey,
                tenantId,
                exclusive,
                cancellationToken);
        }

        if (providerName == RelationalNamedLock.PostgreSqlProvider)
        {
            return await TryAcquirePostgreSqlAsync(
                dbContext,
                projectionLockKey,
                tenantId,
                exclusive,
                cancellationToken);
        }

        if (!dbContext.Database.IsRelational())
        {
            return true;
        }

        if (exclusive && dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Exclusive projection locks require an active transaction.");
        }

        IDbContextTransaction? localTransaction = null;
        if (dbContext.Database.CurrentTransaction is null)
        {
            localTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            DbTransaction transaction = dbContext.Database.CurrentTransaction!.GetDbTransaction();
            string resource = RelationalNamedLock.NormalizeProviderResource(
                providerName,
                $"custom-property-projection:{projectionLockKey}:{tenantId:N}");
            await using DbCommand command = CreateServerTryAcquireCommand(
                dbContext.Database.GetDbConnection(),
                transaction,
                providerName,
                resource,
                exclusive);
            object? result = await command.ExecuteScalarAsync(cancellationToken);
            bool acquired = result is not null
                && result is not DBNull
                && Convert.ToInt32(result, CultureInfo.InvariantCulture) >=
                (providerName == RelationalNamedLock.MySqlProvider ? 1 : 0);
            if (acquired && providerName == RelationalNamedLock.MySqlProvider)
            {
                MySqlNamedLockTransactionInterceptor.Instance.Track(transaction, resource);
            }

            if (localTransaction is not null)
            {
                await localTransaction.CommitAsync(cancellationToken);
            }

            return acquired;
        }
        finally
        {
            if (localTransaction is not null)
            {
                await localTransaction.DisposeAsync();
            }
        }
    }

    private static async Task<bool> TryAcquirePostgreSqlAsync(
        ExploreDbContext dbContext,
        int projectionLockKey,
        Guid tenantId,
        bool exclusive,
        CancellationToken cancellationToken)
    {
        var tenantKey = ComputeStableKey(tenantId.ToString("N"));
        string sql = exclusive
            ? "SELECT pg_try_advisory_xact_lock(@key1, @key2)"
            : "SELECT pg_try_advisory_xact_lock_shared(@key1, @key2)";
        DbConnection connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        if (dbContext.Database.CurrentTransaction is { } currentTransaction)
        {
            command.Transaction = currentTransaction.GetDbTransaction();
        }

        AddParameter(command, "key1", projectionLockKey);
        AddParameter(command, "key2", tenantKey);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is true;
    }

    internal static DbCommand CreateServerTryAcquireCommand(
        DbConnection connection,
        DbTransaction? transaction,
        string providerName,
        string resource,
        bool exclusive)
    {
        DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = providerName switch
        {
            RelationalNamedLock.SqlServerProvider =>
                "DECLARE @result int; EXEC @result = sys.sp_getapplock "
                + "@Resource = @resource, @LockMode = @lockMode, "
                + "@LockOwner = 'Transaction', @LockTimeout = 0; SELECT @result;",
            RelationalNamedLock.MySqlProvider => "SELECT GET_LOCK(@resource, 0)",
            _ => throw new InvalidOperationException(
                $"Unsupported server projection lock provider '{providerName}'."),
        };

        AddParameter(command, "resource", resource);
        if (providerName == RelationalNamedLock.SqlServerProvider)
        {
            AddParameter(command, "lockMode", exclusive ? "Exclusive" : "Shared");
        }

        return command;
    }

    private static bool TryAcquireSqlite(
        ExploreDbContext dbContext,
        int projectionLockKey,
        Guid tenantId,
        bool exclusive,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (exclusive && dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Exclusive projection locks require an active transaction.");
        }

        string resource = $"custom-property-projection:{projectionLockKey}:{tenantId:N}";
        if (dbContext.Database.CurrentTransaction is { } currentTransaction)
        {
            return SqliteProjectionLockTransactionInterceptor.Instance.TryAcquire(
                currentTransaction.GetDbTransaction(),
                resource);
        }

        return SqliteProjectionLockTransactionInterceptor.Instance.TryProbe(resource);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static int ComputeStableKey(string value)
    {
        unchecked
        {
            const int fnvOffsetBasis = unchecked((int)2166136261);
            const int fnvPrime = 16777619;
            var hash = fnvOffsetBasis;
            foreach (char character in value)
            {
                hash ^= character;
                hash *= fnvPrime;
            }

            return hash;
        }
    }
}
