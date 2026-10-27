using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;

namespace Explore.Application.Contracts.Moderation;

public enum ModerationMeasuredOperationsProofKind { Recovery = 1, KeyConvergence = 2, Transport = 3 }
public enum ModerationMeasuredOperationsStoreKind { Canonical = 1, Erasure = 2, Recognition = 3 }

public sealed record ModerationMeasuredOperationsProofScope(
    Guid HostBindingId, Guid TenantId, Guid ProfileId, ModerationActivationBinding Binding,
    string PrimaryConfigurationDigest, string ProviderCode, string ProviderVersion,
    Guid? RecognitionSourceId, Guid? RecognitionAcceptanceId, long RecognitionSourceGeneration,
    int RecognitionPurpose, TimeSpan RecognitionBackupHorizon);

public sealed record ModerationMeasuredOperationsStore(
    ModerationMeasuredOperationsStoreKind Kind, Guid StoreId, long Revision, long Generation, long RequiredPosition);

public sealed record ModerationMeasuredOperationsKey(
    string KeyId, long Generation, string VerificationTag, DateTimeOffset RetainUntil);

/// <summary>Independently acquired current store, replica and key requirements, never derived from a past measurement.</summary>
public sealed record ModerationMeasuredOperationsCurrent(
    ModerationMeasuredOperationsProofScope Scope, ModerationMeasuredOperationsProofKind Kind,
    Guid AuthorityId, long Revision, long WriterGeneration,
    ImmutableArray<ModerationMeasuredOperationsStore> Stores, ImmutableArray<ModerationMeasuredOperationsKey> Keys,
    ImmutableArray<Guid> Replicas, long TransportEpoch, long TransportHead,
    DateTimeOffset ObservedAt, DateTimeOffset ValidUntil);

public sealed record ModerationMeasuredOperationsStoreRecovery(
    ModerationMeasuredOperationsStore Store, Guid BackupId, long BackupPosition, long ReplayedThrough,
    long PendingReconciliations, DateTimeOffset BackupCompletedAt,
    DateTimeOffset RestoreCompletedAt, DateTimeOffset ReconciledAt);

public sealed record ModerationMeasuredOperationsKeyAcknowledgement(
    Guid ReplicaId, ModerationMeasuredOperationsKey Key, DateTimeOffset AcknowledgedAt);

/// <summary>Completed native observations, not authored scenarios, digest attestations or operator success flags.</summary>
public sealed record ModerationMeasuredOperationsMeasurement(
    ModerationMeasuredOperationsProofScope Scope, ModerationMeasuredOperationsProofKind Kind,
    ModerationMeasuredOperationsProofReference Reference, Guid AuthorityId, long CurrentRevision, long CurrentWriterGeneration,
    ImmutableArray<ModerationMeasuredOperationsStoreRecovery> Stores,
    ImmutableArray<ModerationMeasuredOperationsKeyAcknowledgement> Keys,
    long TransportEpoch, long AcknowledgedPosition, long MissingSequenceCount, DateTimeOffset HeartbeatAt,
    Guid? TransportReplicaId = null);

/// <summary>
/// Boundary for the actual recovery, key-convergence and transport authorities. No implementation is
/// registered until those owners exist. Both reads must use the original supplied acquisition, retain
/// native writer fences through its transaction, independently authenticate source provenance and
/// return null on missing owners, gaps or uncertainty. Measurements require real completed restore,
/// replay/erasure reconciliation, key-byte verification and authenticated transport observations.
/// Current requirements must come from actual topology/key state, never the measurement itself.
/// </summary>
public interface IModerationMeasuredOperationsProofSource
{
    Task<ModerationMeasuredOperationsCurrent?> AcquireCurrentAsync(
        IModerationConsumerEvidenceSession session, Guid transactionId, ModerationMeasuredOperationsProofScope scope,
        ModerationMeasuredOperationsProofKind kind, DateTimeOffset observedAt, CancellationToken cancellationToken);

    Task<ModerationMeasuredOperationsMeasurement?> ReadMeasurementAsync(
        IModerationConsumerEvidenceSession session, Guid transactionId, ModerationMeasuredOperationsProofScope scope,
        ModerationMeasuredOperationsProofKind kind, DateTimeOffset observedAt, CancellationToken cancellationToken);
}

/// <summary>Pure exact observation join. A match provides a finite reference, never an activation or conformance verdict.</summary>
public static class ModerationMeasuredOperationsProofJoin
{
    public static ModerationMeasuredOperationsProofReference? Match(
        ModerationMeasuredOperationsCurrent? current, ModerationMeasuredOperationsMeasurement? measured,
        ModerationMeasuredOperationsProofScope scope, ModerationMeasuredOperationsProofKind kind, DateTimeOffset now)
    {
        if (current is null || measured is null || current.Scope != scope || measured.Scope != scope
            || current.Kind != kind || measured.Kind != kind || current.ObservedAt != now
            || now.Offset != TimeSpan.Zero || now == DateTimeOffset.MinValue || now == DateTimeOffset.MaxValue
            || current.AuthorityId == Guid.Empty || current.AuthorityId.Version != 7
            || current.Revision is < 1 or long.MaxValue || current.WriterGeneration is < 1 or long.MaxValue
            || current.ValidUntil.Offset != TimeSpan.Zero || current.ValidUntil <= now || current.ValidUntil == DateTimeOffset.MaxValue
            || measured.AuthorityId != current.AuthorityId || measured.CurrentRevision != current.Revision
            || measured.CurrentWriterGeneration != current.WriterGeneration || !measured.Reference.IsCurrent(now)
            || measured.Reference.WriterGeneration < current.WriterGeneration
            || measured.Reference.ValidUntil > current.ValidUntil
            || current.Stores.IsDefault || current.Keys.IsDefault || current.Replicas.IsDefault
            || measured.Stores.IsDefault || measured.Keys.IsDefault)
            return null;
        var matches = kind switch
        {
            ModerationMeasuredOperationsProofKind.Recovery => Recovery(current, measured, scope, now),
            ModerationMeasuredOperationsProofKind.KeyConvergence => Keys(current, measured, scope, now),
            ModerationMeasuredOperationsProofKind.Transport => Transport(current, measured, scope, now),
            _ => false
        };
        return matches ? measured.Reference : null;
    }

    private static bool Recovery(ModerationMeasuredOperationsCurrent current,
        ModerationMeasuredOperationsMeasurement measured, ModerationMeasuredOperationsProofScope scope, DateTimeOffset now)
    {
        // Canonical and erasure authority are never waived, including profiles with no recognition.
        var required = scope.RecognitionPurpose == 0
            ? new[] { ModerationMeasuredOperationsStoreKind.Canonical, ModerationMeasuredOperationsStoreKind.Erasure }
            : [ModerationMeasuredOperationsStoreKind.Canonical, ModerationMeasuredOperationsStoreKind.Erasure,
                ModerationMeasuredOperationsStoreKind.Recognition];
        if (current.Stores.Length != required.Length || measured.Stores.Length != required.Length
            || !current.Stores.Select(row => row.Kind).Order().SequenceEqual(required.Order())
            || current.Stores.Select(row => row.StoreId).Distinct().Count() != required.Length
            || measured.Stores.Select(row => row.Store.Kind).Distinct().Count() != required.Length
            || current.Keys.Length != 0 || measured.Keys.Length != 0) return false;
        foreach (var store in current.Stores)
        {
            var witness = measured.Stores.SingleOrDefault(row => row.Store.Kind == store.Kind);
            if (store.StoreId == Guid.Empty || store.StoreId.Version != 7 || store.Revision < 1 || store.Generation < 1
                || store.RequiredPosition < 0 || witness is null || witness.Store != store
                || witness.BackupId == Guid.Empty || witness.BackupId.Version != 7
                || witness.BackupPosition < 0 || witness.BackupPosition > store.RequiredPosition
                || witness.ReplayedThrough != store.RequiredPosition || witness.PendingReconciliations != 0
                || !Utc(witness.BackupCompletedAt) || !Utc(witness.RestoreCompletedAt) || !Utc(witness.ReconciledAt)
                || witness.BackupCompletedAt >= witness.RestoreCompletedAt
                || witness.RestoreCompletedAt > witness.ReconciledAt || witness.ReconciledAt > measured.Reference.MeasuredAt
                || witness.ReconciledAt > now) return false;
        }
        return true;
    }

    private static bool Keys(ModerationMeasuredOperationsCurrent current,
        ModerationMeasuredOperationsMeasurement measured, ModerationMeasuredOperationsProofScope scope, DateTimeOffset now)
    {
        if (scope.RecognitionPurpose is < 1 or > 3 || scope.RecognitionSourceId is null
            || scope.RecognitionAcceptanceId is null || scope.RecognitionSourceGeneration < 1
            || scope.RecognitionBackupHorizon <= TimeSpan.Zero || scope.RecognitionBackupHorizon > DateTimeOffset.MaxValue - now
            || current.Keys.Length is < 1 or > 16 || current.Replicas.Length is < 1 or > 64
            || current.Replicas.Any(id => id == Guid.Empty || id.Version != 7)
            || current.Replicas.Distinct().Count() != current.Replicas.Length
            || current.Keys.Select(row => row.KeyId).Distinct(StringComparer.Ordinal).Count() != current.Keys.Length
            || measured.Keys.Length != current.Keys.Length * current.Replicas.Length
            || current.Stores.Length != 0 || measured.Stores.Length != 0) return false;
        foreach (var key in current.Keys)
        {
            if (string.IsNullOrWhiteSpace(key.KeyId) || key.KeyId.Length > 128 || key.Generation < 1
                || !ModerationMeasurementCatalogue.IsDigest(key.VerificationTag)
                || !Utc(key.RetainUntil) || key.RetainUntil < now + scope.RecognitionBackupHorizon) return false;
            foreach (var replica in current.Replicas)
            {
                var acknowledgements = measured.Keys.Where(row => row.ReplicaId == replica && row.Key.KeyId == key.KeyId).ToArray();
                if (acknowledgements.Length != 1 || acknowledgements[0].Key != key
                    || !Utc(acknowledgements[0].AcknowledgedAt)
                    || acknowledgements[0].AcknowledgedAt > measured.Reference.MeasuredAt) return false;
            }
        }
        return true;
    }

    private static bool Transport(ModerationMeasuredOperationsCurrent current,
        ModerationMeasuredOperationsMeasurement measured, ModerationMeasuredOperationsProofScope scope, DateTimeOffset now) =>
        scope.Binding.Mode == ModerationNativeMode.RemoteStrict && current.Replicas.Length == 1
        && current.Replicas[0] != Guid.Empty && current.Replicas[0].Version == 7
        && measured.TransportReplicaId == current.Replicas[0]
        && current.Stores.Length == 0 && measured.Stores.Length == 0 && current.Keys.Length == 0 && measured.Keys.Length == 0
        && current.TransportEpoch > 0 && current.TransportHead >= 0
        && measured.TransportEpoch == current.TransportEpoch && measured.AcknowledgedPosition == current.TransportHead
        && measured.MissingSequenceCount == 0 && Utc(measured.HeartbeatAt)
        && measured.HeartbeatAt <= measured.Reference.MeasuredAt && now - measured.HeartbeatAt <= TimeSpan.FromMinutes(1);

    private static bool Utc(DateTimeOffset value) => value.Offset == TimeSpan.Zero
        && value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue;
}
