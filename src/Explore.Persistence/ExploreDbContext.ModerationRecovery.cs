using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence;

public partial class ExploreDbContext
{
    private const string ModerationRecoveryWriterFence = "moderation.canonical-recovery-store";
    private ModerationRecoveryStoreState? _stagedModerationRecovery;
    private (Guid Id, long Revision, long Generation, long Position)? _stagedModerationRecoveryCoordinates;
    private Guid? _moderationRecoveryMutationTransactionId;

    private bool HasLostModerationRecoveryAdvancement(Guid transactionId) =>
        _moderationRecoveryMutationTransactionId == transactionId
        && (_stagedModerationRecovery is null
            || !IsOwnedModerationRecoveryAdvancement(Entry(_stagedModerationRecovery)));

    private bool IsOwnedModerationRecoveryAdvancement(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry) =>
        entry.Entity is ModerationRecoveryStoreState state
        && entry.State is EntityState.Added or EntityState.Modified or EntityState.Unchanged
        && ReferenceEquals(state, _stagedModerationRecovery)
        && _stagedModerationRecoveryCoordinates == Coordinates(state)
        && Database.CurrentTransaction is { } transaction
        && _moderationRecoveryMutationTransactionId == transaction.TransactionId
        && Equals(entry.Property("InstallationSlot").CurrentValue, 1);

    private async Task PrepareModerationRecoveryAsync(bool bulkMutation, CancellationToken token)
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries<ModerationRecoveryStoreState>()
                     .Where(row => row.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            if (entry.State == EntityState.Deleted || !ReferenceEquals(entry.Entity, _stagedModerationRecovery)
                || _stagedModerationRecoveryCoordinates != Coordinates(entry.Entity))
                throw new InvalidOperationException("Canonical recovery coordinates are owned by the native save boundary.");

        var changed = bulkMutation || ChangeTracker.Entries().Any(entry =>
            entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
            && entry.Entity is not ModerationRecoveryStoreState
            && (entry.Metadata.ClrType.Namespace == "ISLAMU.Moderation.Domain"
                || entry.Metadata.ClrType.Namespace == "Explore.Domain"
                && (entry.Metadata.ClrType.Name.StartsWith("Moderation", StringComparison.Ordinal)
                    || entry.Metadata.ClrType.Name.StartsWith("EventResourceModeration", StringComparison.Ordinal))));
        if (!changed)
        {
            if (ChangeTracker.Entries<ModerationRecoveryStoreState>()
                .Any(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
                throw new InvalidOperationException("Recovery coordinates cannot commit without their canonical mutation.");
            return;
        }
        if (!Database.IsRelational() || Database.CurrentTransaction is null)
            throw new InvalidOperationException("Canonical recovery advancement requires the native relational transaction.");
        await using var fence = await RelationalNamedLock.AcquireTransactionAsync(this, ModerationRecoveryWriterFence, token);
        var persisted = await Set<ModerationRecoveryStoreState>().AsNoTracking().SingleOrDefaultAsync(token);
        var tracked = ChangeTracker.Entries<ModerationRecoveryStoreState>().SingleOrDefault();
        ModerationRecoveryStoreState state;
        if (persisted is null)
        {
            state = ModerationRecoveryStoreState.Start(tracked?.Entity.Id ?? Guid.CreateVersion7());
            if (tracked is not null) tracked.State = EntityState.Detached;
            Add(state);
        }
        else
        {
            persisted.ValidatePersistedState();
            if (tracked is not null)
            {
                if (tracked.Entity.Id != persisted.Id)
                    throw new InvalidOperationException("The canonical recovery store identity changed.");
                tracked.CurrentValues.SetValues(persisted);
                tracked.OriginalValues.SetValues(persisted);
                tracked.State = EntityState.Unchanged;
                state = tracked.Entity;
            }
            else
            {
                state = persisted;
                Attach(state);
            }
        }
        state.Advance(state.Revision, state.Position);
        _stagedModerationRecovery = state;
        _stagedModerationRecoveryCoordinates = Coordinates(state);
        _moderationRecoveryMutationTransactionId = Database.CurrentTransaction.TransactionId;
    }

    /// <summary>Accounts for a successful bulk-only mutation in its existing native transaction.</summary>
    internal Task<int> RecordModerationRecoveryBulkMutationAsync(CancellationToken token) =>
        SaveWithStorageReferencesAsync(async acceptChanges =>
        {
            PrepareTrackedEntities();
            await PrepareModerationRecoveryAsync(bulkMutation: true, token);
            return await base.SaveChangesAsync(acceptChanges, token);
        }, acceptAllChangesOnSuccess: true, token);

    /// <summary>Acquires metadata only on the original live evidence session, never a detached request.</summary>
    internal async Task<ModerationRecoveryStoreState?> AcquireModerationRecoveryStateAsync(
        ModerationConsumerEvidenceSession session, Guid transactionId, DateTimeOffset observedAt, CancellationToken token)
    {
        var native = await session.AcquireNativeAsync(transactionId, observedAt, token);
        if (native is null || !ReferenceEquals(native.Context, this)
            || !ReferenceEquals(native.Transaction, Database.CurrentTransaction) || ChangeTracker.HasChanges()
            || _moderationRecoveryMutationTransactionId == transactionId)
            return null;
        await using var fence = await RelationalNamedLock.AcquireTransactionAsync(this, ModerationRecoveryWriterFence, token);
        var state = await Set<ModerationRecoveryStoreState>().AsNoTracking().SingleOrDefaultAsync(token);
        if (state is null) return null;
        state.ValidatePersistedState();
        return await session.AcquireNativeAsync(transactionId, observedAt, token) is { } current
            && ReferenceEquals(current.Context, this) && ReferenceEquals(current.Transaction, native.Transaction)
            && !ChangeTracker.HasChanges() && _moderationRecoveryMutationTransactionId != transactionId ? state : null;
    }

    /// <summary>
    /// Reads committed file-backed SQLite coordinates through a separate read-only connection.
    /// The original evidence transaction remains the authority and keeps its writer fences.
    /// This grants no private snapshot access and produces no backup or restoration verdict.
    /// </summary>
    internal async Task<ModerationRecoveryStoreState?> ReadSqliteCommittedModerationRecoveryStateAsync(
        ModerationConsumerEvidenceSession session, Guid transactionId, DateTimeOffset observedAt, CancellationToken token)
    {
        if (Database.ProviderName != RelationalNamedLock.SqliteProvider
            || Database.GetDbConnection() is not SqliteConnection source)
            return null;
        var original = await AcquireModerationRecoveryStateAsync(session, transactionId, observedAt, token);
        if (original is null) return null;
        var settings = new SqliteConnectionStringBuilder(source.ConnectionString);
        if (settings.Mode == SqliteOpenMode.Memory || string.IsNullOrWhiteSpace(source.DataSource)
            || source.DataSource == ":memory:")
            return null;
        settings.Mode = SqliteOpenMode.ReadOnly;
        settings.Cache = SqliteCacheMode.Private;
        settings.Pooling = false;
        await using var committed = new SqliteConnection(settings.ToString());
        await committed.OpenAsync(token);
        var entity = Model.FindEntityType(typeof(ModerationRecoveryStoreState))
            ?? throw new InvalidOperationException("The canonical recovery store is not mapped.");
        var tableName = entity.GetTableName()
            ?? throw new InvalidOperationException("The canonical recovery store has no table.");
        var table = StoreObjectIdentifier.Table(tableName, entity.GetSchema());
        var sql = this.GetService<ISqlGenerationHelper>();
        string Column(string name) => sql.DelimitIdentifier(
            entity.FindProperty(name)?.GetColumnName(table)
                ?? throw new InvalidOperationException("The canonical recovery coordinate is not mapped."));
        await using var command = committed.CreateCommand();
        command.CommandText = $"SELECT {Column(nameof(ModerationRecoveryStoreState.Id))}, "
            + $"{Column(nameof(ModerationRecoveryStoreState.Revision))}, "
            + $"{Column(nameof(ModerationRecoveryStoreState.Generation))}, "
            + $"{Column(nameof(ModerationRecoveryStoreState.Position))} "
            + $"FROM {sql.DelimitIdentifier(tableName, entity.GetSchema())} "
            + $"WHERE {Column("InstallationSlot")} = 1";
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return null;
        var coordinates = (reader.GetGuid(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3));
        if (coordinates != Coordinates(original) || await reader.ReadAsync(token))
            return null;
        // Requalification uses the original context/transaction, never the committed reader.
        var current = await AcquireModerationRecoveryStateAsync(session, transactionId, observedAt, token);
        return current is not null && Coordinates(current) == coordinates
            && ReferenceEquals(Database.GetDbConnection(), source) ? current : null;
    }

    private static (Guid Id, long Revision, long Generation, long Position) Coordinates(ModerationRecoveryStoreState state) =>
        (state.Id, state.Revision, state.Generation, state.Position);
}
