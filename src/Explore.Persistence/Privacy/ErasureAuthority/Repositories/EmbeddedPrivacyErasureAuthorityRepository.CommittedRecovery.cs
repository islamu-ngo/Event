using Explore.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Privacy.ErasureAuthority.Repositories;

public sealed partial class EmbeddedPrivacyErasureAuthorityRepository
{
    /// <summary>
    /// Reads only committed retained-store coordinates on a separately derived SQLite connection.
    /// The original identity writer gate remains the authority; neither connections nor private
    /// authority facts escape. This is not backup, restoration or reconciliation evidence.
    /// </summary>
    internal Task<PrivacyErasureRecoveryState?> ReadSqliteCommittedRecoveryStateAsync(
        CancellationToken cancellationToken) =>
        ReadSqliteCommittedRecoveryStateAsync(requireReplayInput: false, cancellationToken);

    /// <summary>
    /// Acquires committed replay-input coordinates only after reading every retained sequence
    /// above the original floor through the original high-water mark. Gaps, pseudonymized
    /// identities above the floor and rows beyond the head fail closed. Only sequence and hold
    /// metadata are read; no private facts, artifact, completed replay or recovery proof is produced.
    /// The result remains usable only inside the original writer gate and must be reacquired
    /// before a caller commits; it does not authorize a restore whose checkpoint is below the floor.
    /// </summary>
    internal Task<PrivacyErasureRecoveryState?> ReadSqliteCommittedReplayInputStateAsync(
        CancellationToken cancellationToken) =>
        ReadSqliteCommittedRecoveryStateAsync(requireReplayInput: true, cancellationToken);

    private async Task<PrivacyErasureRecoveryState?> ReadSqliteCommittedRecoveryStateAsync(
        bool requireReplayInput, CancellationToken cancellationToken)
    {
        var gate = _recoveryGate.Value
            ?? throw new InvalidOperationException("privacy_erasure_recovery_gate_required");
        var original = await AcquireRecoveryStateAsync(cancellationToken);
        var db = _identityGate.Value!;
        if (db.Database.GetDbConnection() is not SqliteConnection source)
            return null;
        var settings = new SqliteConnectionStringBuilder(source.ConnectionString);
        if (settings.Mode == SqliteOpenMode.Memory || string.IsNullOrWhiteSpace(source.DataSource)
            || source.DataSource == ":memory:")
            return null;
        settings.Mode = SqliteOpenMode.ReadOnly;
        settings.Cache = SqliteCacheMode.Private;
        settings.Pooling = false;
        await using var committed = new SqliteConnection(settings.ToString());
        await committed.OpenAsync(cancellationToken);
        var entity = db.Model.FindEntityType(typeof(PrivacyErasureCounter))
            ?? throw new InvalidOperationException("The retained recovery store is not mapped.");
        var tableName = entity.GetTableName()
            ?? throw new InvalidOperationException("The retained recovery store has no table.");
        var table = StoreObjectIdentifier.Table(tableName, entity.GetSchema());
        var sql = db.GetService<ISqlGenerationHelper>();
        string Column(string name) => sql.DelimitIdentifier(
            entity.FindProperty(name)?.GetColumnName(table)
                ?? throw new InvalidOperationException("The retained recovery coordinate is not mapped."));
        await using var command = committed.CreateCommand();
        command.CommandText = $"SELECT {Column(nameof(PrivacyErasureCounter.StoreId))}, "
            + $"{Column(nameof(PrivacyErasureCounter.Revision))}, "
            + $"{Column(nameof(PrivacyErasureCounter.RecoveryGeneration))}, "
            + $"{Column(nameof(PrivacyErasureCounter.LastSequence))}, "
            + $"{Column(nameof(PrivacyErasureCounter.RetainedFloorSequence))}, "
            + $"{Column(nameof(PrivacyErasureCounter.IdentityKeyId))}, "
            + $"{Column(nameof(PrivacyErasureCounter.IdentityKeyVerificationTag))} "
            + $"FROM {sql.DelimitIdentifier(tableName, entity.GetSchema())} "
            + $"WHERE {Column(nameof(PrivacyErasureCounter.Singleton))} = 1";
        PrivacyErasureRecoveryState observed;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
                return null;
            observed = new PrivacyErasureRecoveryState(
                reader.GetGuid(0), reader.GetInt64(1), reader.GetInt64(2),
                new PrivacyErasureAuthorityState(reader.GetInt64(3), reader.GetInt64(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6)));
            if (observed != original || await reader.ReadAsync(cancellationToken))
                return null;
        }
        if (requireReplayInput
            && !await HasCommittedReplayInputAsync(db, committed, observed.AuthorityState, cancellationToken))
            return null;
        // Currentness is checked on the original owner/context/transaction, never the reader.
        gate.RequireActive();
        var current = await AcquireRecoveryStateAsync(cancellationToken);
        return current == observed && ReferenceEquals(_recoveryGate.Value, gate)
            && ReferenceEquals(_identityGate.Value, db)
            && ReferenceEquals(db.Database.GetDbConnection(), source) ? current : null;
    }

    private static async Task<bool> HasCommittedReplayInputAsync(
        EmbeddedPrivacyErasureAuthorityDbContext db, SqliteConnection committed,
        PrivacyErasureAuthorityState state, CancellationToken cancellationToken)
    {
        var entity = db.Model.FindEntityType(typeof(PrivacyErasureIntent))
            ?? throw new InvalidOperationException("The retained replay input is not mapped.");
        var tableName = entity.GetTableName()
            ?? throw new InvalidOperationException("The retained replay input has no table.");
        var table = StoreObjectIdentifier.Table(tableName, entity.GetSchema());
        var sql = db.GetService<ISqlGenerationHelper>();
        string Column(string name) => sql.DelimitIdentifier(
            entity.FindProperty(name)?.GetColumnName(table)
                ?? throw new InvalidOperationException("The retained replay coordinate is not mapped."));
        var sequence = Column(nameof(PrivacyErasureIntent.AuthoritySequence));
        await using var command = committed.CreateCommand();
        command.CommandText = $"SELECT {sequence}, {Column(nameof(PrivacyErasureIntent.IsLegalHoldPseudonymized))} "
            + $"FROM {sql.DelimitIdentifier(tableName, entity.GetSchema())} "
            + $"WHERE {sequence} > $floor ORDER BY {sequence}";
        command.Parameters.AddWithValue("$floor", state.RetainedFloorSequence);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var position = state.RetainedFloorSequence;
        while (await reader.ReadAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (position >= state.HighWaterSequence || reader.GetInt64(0) != position + 1 || reader.GetBoolean(1))
                return false;
            position++;
        }
        return position == state.HighWaterSequence;
    }
}
