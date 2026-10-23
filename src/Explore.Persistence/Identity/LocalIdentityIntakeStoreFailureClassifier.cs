using System.Data.Common;
using Explore.Application.Contracts.Identity;
using Explore.Persistence.Database;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Npgsql;

namespace Explore.Persistence.Identity;

internal static class LocalIdentityIntakeStoreFailureClassifier
{
    internal static LocalIdentityIntakeFailure? Classify(DbContext context, Exception exception)
    {
        // Only used by the idle native intake owner before its commit boundary.
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is OperationCanceledException or ArgumentException or InvalidOperationException) return null;

        if (exception is DbUpdateConcurrencyException)
            return LocalIdentityIntakeFailure.Conflict;
        var update = exception as DbUpdateException
            ?? (exception is DbException database ? new DbUpdateException("Native intake conflict.", database) : null);
        if (update is not null && RegistrationUniqueConflictClassifier.IsProviderUniqueConflict(update))
            return RegistrationUniqueConflictClassifier.IsExpectedConflict(update,
                [RelationalConstraintDescriptorResolver.PrimaryKey<LocalIdentityLifecycleOperation>(context)])
                ? LocalIdentityIntakeFailure.Conflict : null;
        if (LocalIdentityIntakeFailureClassifier.IsTransientStoreFailure(exception))
            return LocalIdentityIntakeFailure.StoreUnavailable;

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqliteException { SqliteErrorCode: 5 or 6 }
                || current is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected }
                || current is MySqlException { Number: 1205 or 1213 }
                || current is SqlException { Number: 1205 })
                return LocalIdentityIntakeFailure.Conflict;
        }
        return null;
    }
}
