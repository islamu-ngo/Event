using System.Data;
using System.Data.Common;
using System.Runtime.ExceptionServices;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MySqlConnector;
using Npgsql;

namespace Explore.Persistence;

public sealed partial class EfCoreUnitOfWork : IUnitOfWork, IPrivacyIdentityEnrollmentTransaction, ISerializableAttemptOwner
{
    private const int BootstrapConvergenceAttemptLimit = 4;
    private const int SqliteConstraintPrimaryKey = 1555;
    private const int SqliteConstraintUnique = 2067;
    private readonly ExploreDbContext _dbContext;
    private readonly Func<IExecutionStrategy> _createExecutionStrategy;
    private readonly Func<CancellationToken, Task<IDbContextTransaction>> _beginTransaction;

    public EfCoreUnitOfWork(ExploreDbContext dbContext)
        : this(
            dbContext,
            dbContext.Database.CreateExecutionStrategy,
            dbContext.Database.BeginTransactionAsync)
    {
    }

    internal EfCoreUnitOfWork(
        ExploreDbContext dbContext,
        Func<IExecutionStrategy> createExecutionStrategy,
        Func<CancellationToken, Task<IDbContextTransaction>> beginTransaction)
    {
        _dbContext = dbContext;
        _createExecutionStrategy = createExecutionStrategy;
        _beginTransaction = beginTransaction;
    }

    // Void overload delegates to generic — single execution path, no duplication
    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default)
        => ExecuteInTransactionAsync<object?>(async innerCt =>
        {
            await operation(innerCt);
            return null;
        }, ct);

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        // MySQL's default RepeatableRead uses a historical snapshot for ordinary
        // dependency queries even after a current-read row fence. Serializable
        // gives source fanout current locking reads and whole-attempt replay.
        if (_dbContext.Database.ProviderName == Database.RelationalNamedLock.MySqlProvider)
            return await ExecuteSerializableAsync(operation, ct);
        return await ExecuteCoreAsync(operation, isolationLevel: null, ct);
    }

    public async Task<T> ExecuteSerializableAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct = default)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                return await ExecuteCoreAsync(operation, IsolationLevel.Serializable, ct);
            }
            catch (Exception exception) when (attempt < 4 && IsSerializableRetryConflict(exception))
            {
                // ExecuteCore has rolled back and cleared operation writes. Transaction-start
                // conflicts have no writes, but may still have tracked state from the caller.
                _dbContext.ChangeTracker.Clear();
            }
        }
    }

    Task<T> ISerializableAttemptOwner.ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
            throw new InvalidOperationException("Prepared authority requires a relational Serializable owner.");
        if (ExecutionStrategy.Current?.RetriesOnFailure == true)
            throw new InvalidOperationException("Prepared authority cannot execute inside a replaying outer strategy.");
        return ExecuteCoreAsync(operation, IsolationLevel.Serializable, cancellationToken, allowReplay: false);
    }

    public Task<T> ExecuteBootstrapConvergenceAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct = default) =>
        _dbContext.IdentityFenceOwnsTransaction
            ? ExecuteCoreAsync(operation, IsolationLevel.Serializable, ct)
            : ExecuteBootstrapConflictRetryAsync(() => ExecuteCoreAsync(operation, IsolationLevel.Serializable, ct), ct);

    public Task<T> ExecuteRecognitionEnrollmentAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task> beforeCommit,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(beforeCommit);
        return _dbContext.IdentityFenceOwnsTransaction
            ? ExecuteCoreAsync(operation, IsolationLevel.Serializable, ct, beforeCommit)
            : ExecuteBootstrapConflictRetryAsync(
                () => ExecuteCoreAsync(operation, IsolationLevel.Serializable, ct, beforeCommit), ct);
    }

    public Task<T> ExecuteReadCommittedAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct = default) =>
        ExecuteCoreAsync(operation,
            _dbContext.Database.IsSqlite() ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted, ct);

    // Receipt-owning callers reconcile the previous outcome before replaying the whole privacy/primary unit.
    internal Task<T> ExecuteRecognitionEnrollmentAttemptAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task> beforeCommit,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(beforeCommit);
        return ExecuteCoreAsync(operation, IsolationLevel.Serializable, ct, beforeCommit);
    }

    internal static async Task<T> ExecuteBootstrapConflictRetryAsync<T>(
        Func<Task<T>> operation, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                return await operation();
            }
            catch (Exception exception) when (
                attempt < BootstrapConvergenceAttemptLimit
                && IsProviderWriteConflict(exception))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), ct);
            }
        }
    }

    private async Task<T> ExecuteCoreAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        IsolationLevel? isolationLevel,
        CancellationToken ct,
        Func<CancellationToken, Task>? beforeCommit = null,
        bool allowReplay = true)
    {
        // Co-located SQLite shares the authority write transaction: the gate
        // commits both contexts after this operation returns.
        if (_dbContext.IdentityFenceOwnsTransaction && _dbContext.Database.CurrentTransaction is not null)
        {
            if (_dbContext.IdentityFenceTransactionFailed)
                throw new InvalidOperationException("The identity enrollment transaction has failed.");
            try
            {
                _dbContext.HumanCommitAttemptOwnsTransaction = !allowReplay;
                var result = await operation(ct);
                if (beforeCommit is not null)
                    await _dbContext.SaveChangesAsync(ct);
                if (_dbContext.StorageReferenceTransactionFailed)
                    throw new ConcurrencyConflictException(ConcurrencyConflictException.ConcurrentUpdate,
                        "The storage reference transaction has failed.");
                return result;
            }
            catch
            {
                // A handler may translate this exception into a failure response.
                // The owning authority gate must still roll back the entire unit.
                _dbContext.IdentityFenceTransactionFailed = true;
                _dbContext.ChangeTracker.Clear();
                throw;
            }
        }

        // Nested transaction guard — fail fast with a deterministic error
        if (_dbContext.Database.CurrentTransaction != null)
            throw new InvalidOperationException(
                "ExecuteInTransactionAsync cannot be called while a transaction is already active. " +
                "Nested transactions are not supported. Ensure only one UoW transaction scope per handler.");

        // InMemory provider does not support transactions or execution strategies — run directly
        if (_dbContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            try
            {
                var result = await operation(ct);
                if (beforeCommit is not null)
                {
                    await _dbContext.SaveChangesAsync(ct);
                    await beforeCommit(ct);
                }
                return result;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw TranslateConcurrencyException(ex);
            }
        }

        var strategy = allowReplay ? _createExecutionStrategy() : new SingleAttemptExecutionStrategy(_dbContext);
        return await strategy.ExecuteAsync(async () =>
        {
            var transaction = isolationLevel.HasValue
                ? await _dbContext.Database.BeginTransactionAsync(isolationLevel.Value, ct)
                : await _beginTransaction(ct);
            bool commitAcknowledged = false;
            Exception? primary = null;
            T completedResult = default!;
            try
            {
                completedResult = await ExecuteOwnedTransactionAsync();
            }
            catch (Exception failure)
            {
                primary = failure;
            }
            try { await transaction.DisposeAsync(); }
            catch (Exception cleanup)
            {
                Exception evidence = primary is null ? cleanup : new AggregateException(primary, cleanup);
                if (commitAcknowledged)
                    throw new TransactionCommittedCleanupException(evidence);
                if (primary is HumanMutationOutcomeUnknownException)
                    throw new HumanMutationOutcomeUnknownException(evidence);
                if (primary is not null) throw evidence;
                throw;
            }
            if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
            return completedResult;

            async Task<T> ExecuteOwnedTransactionAsync()
            {
                DbTransaction physicalTransaction = transaction.GetDbTransaction();
                bool commitAttempted = false;
                try
                {
                    _dbContext.HumanCommitAttemptOwnsTransaction = !allowReplay;
                    var result = await operation(ct);
                    if (beforeCommit is not null)
                        await _dbContext.SaveChangesAsync(ct);
                    if (_dbContext.StorageReferenceTransactionFailed)
                        throw new ConcurrencyConflictException(ConcurrencyConflictException.ConcurrentUpdate,
                            "The storage reference transaction has failed.");
                    await _dbContext.FlushDisclosureAsync(ct);
                    await _dbContext.ValidateTrustedSetupSettingsSourcesAsync(ct);
                    await _dbContext.ValidateHumanCommitAdmissionsAsync(ct);
                    if (beforeCommit is not null)
                        await beforeCommit(ct);
                    _dbContext.ValidateHumanCommitAdmissionTimes();
                    _dbContext.ValidateTrustedSetupSettingsTimes();
                    AdmissionTargetTransaction.RequireCurrentIfHeld(_dbContext);
                    commitAttempted = true;
                    await transaction.CommitAsync(ct);
                    commitAcknowledged = true;
                    return result;
                }
                catch (Exception ex) when (!allowReplay && commitAttempted
                    && (!IsSerializableRetryConflict(ex)
                        || !RelationalConnectionIdentity.Capture(_dbContext).Owns(physicalTransaction)))
                {
                    // A completed/ended provider transaction cannot be certified as rolled back
                    // by a later exception or rollback call. Retain unknown settlement explicitly.
                    await RollbackAndClearTrackingAsync(transaction);
                    throw new HumanMutationOutcomeUnknownException(ex);
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    var translatedException = TranslateConcurrencyException(ex);
                    if (!allowReplay)
                    {
                        if (!await RollbackAndClearTrackingAsync(transaction, confirmRollback: true))
                            throw new HumanMutationOutcomeUnknownException(ex);
                        throw translatedException;
                    }
                    await RollbackAndClearTrackingAsync(transaction);
                    throw translatedException;
                }
                catch (Exception ex) when (
                    isolationLevel == IsolationLevel.Serializable
                    && IsProviderWriteConflict(ex))
                {
                    if (!allowReplay && IsSerializableRetryConflict(ex))
                    {
                        bool acknowledged = await RollbackAndClearTrackingAsync(transaction, confirmRollback: true);
                        if (acknowledged)
                            throw new HumanMutationAttemptRolledBackException(ex);
                        throw new HumanMutationOutcomeUnknownException(ex);
                    }
                    await RollbackAndClearTrackingAsync(transaction);
                    throw new ConcurrencyConflictException(
                        ConcurrencyConflictException.ConcurrentUpdate,
                        "A concurrent writer changed the serializable transaction.",
                        innerException: ex);
                }
                catch (Exception ex)
                {
                    if (!allowReplay)
                    {
                        if (!await RollbackAndClearTrackingAsync(transaction, confirmRollback: true))
                            throw new HumanMutationOutcomeUnknownException(ex);
                        throw;
                    }
                    await RollbackAndClearTrackingAsync(transaction);
                    throw;
                }
                finally
                {
                    _dbContext.ResetHumanCommitAdmissions();
                    _dbContext.ResetTrustedSetupSettingsAdmissions();
                }
            }
        });
    }

    private sealed class SingleAttemptExecutionStrategy(DbContext context)
        : ExecutionStrategy(context, maxRetryCount: 0, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => false;
    }

    private async Task<bool> RollbackAndClearTrackingAsync(IDbContextTransaction transaction, bool confirmRollback = false)
    {
        try
        {
            if (!confirmRollback)
            {
                await RollbackBestEffortAsync(transaction);
                return false;
            }
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return true;
            }
            catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException or DbException)
            {
                return false;
            }
        }
        finally
        {
            _dbContext.ChangeTracker.Clear();
            _dbContext.ResetDisclosureMutations();
        }
    }

    private static async Task RollbackBestEffortAsync(IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (ObjectDisposedException)
        {
            // Preserve the original operation exception when EF/Npgsql already disposed the failed transaction.
        }
        catch (InvalidOperationException)
        {
            // Preserve the original operation exception when the transaction is no longer rollback-ready.
        }
        catch (DbException)
        {
        }
    }

    private static ConcurrencyConflictException TranslateConcurrencyException(DbUpdateConcurrencyException ex)
    {
        var entry = ex.Entries.Count > 0 ? ex.Entries[0] : null;
        var entityType = entry?.Entity.GetType().Name;
        string? entityId = null;
        if (entry?.Metadata.FindPrimaryKey() is { } pk)
        {
            entityId = string.Join(":", pk.Properties.Select(p =>
                entry.Property(p.Name).CurrentValue?.ToString() ?? string.Empty));
        }

        return new ConcurrencyConflictException(
            ConcurrencyConflictException.ConcurrentUpdate,
            $"The {entityType ?? "entity"} was modified by another request. Reload and retry.",
            entityType,
            entityId,
            ex);
    }

    private static bool IsProviderWriteConflict(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is TransactionCommittedCleanupException or HumanMutationOutcomeUnknownException)
                return false;
            if (current is SqliteException { SqliteErrorCode: 5 or 6 }
                || current is SqliteException
                {
                    SqliteExtendedErrorCode: SqliteConstraintPrimaryKey or SqliteConstraintUnique
                }
                || current is PostgresException
                {
                    SqlState: PostgresErrorCodes.SerializationFailure
                        or PostgresErrorCodes.DeadlockDetected
                        or PostgresErrorCodes.UniqueViolation
                }
                || current is MySqlException { Number: 1062 or 1205 or 1213 }
                || current is SqlException { Number: 1205 or 2601 or 2627 })
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSerializableRetryConflict(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is TransactionCommittedCleanupException or HumanMutationOutcomeUnknownException)
                return false;
            if (current is SqliteException { SqliteErrorCode: 5 or 6 }
                || current is PostgresException
                {
                    SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected
                }
                || current is MySqlException { Number: 1205 or 1213 }
                || current is SqlException { Number: 1205 })
                return true;
        }
        return false;
    }
}
