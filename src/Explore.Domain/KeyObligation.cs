namespace Explore.Domain;

/// <summary>
/// Material-free original capture obligation. Its deadline comes from the immutable original
/// retention ceiling plus that source's backup horizon and margin, never rotation or recapture time.
/// Pruning tokens does not discharge the backup obligation or permit rehashing erased identifiers.
/// </summary>
public sealed class KeyObligation
{
    private KeyObligation() { }
    public Guid Id { get; private set; }
    public Guid IntentId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid SourceId { get; private set; }
    public string KeyId { get; private set; } = string.Empty;
    public long KeyGeneration { get; private set; }
    public string VerificationTag { get; private set; } = string.Empty;
    public string CaptureMaterialReceipt { get; private set; } = string.Empty;
    public string MaterialReceipt { get; private set; } = string.Empty;
    public DateTimeOffset OriginalCapturedAt { get; private set; }
    public DateTimeOffset OriginalRetainUntil { get; private set; }
    public long BackupHorizonTicks { get; private set; }
    public long BackupSafetyMarginTicks { get; private set; }
    public long ReceiptRetentionTicks { get; private set; }
    public DateTimeOffset ReadableUntil { get; private set; }
    public long? AuthoritySequence { get; private set; }
    public DateTimeOffset? MaterialPrunedAt { get; private set; }
    public DateTimeOffset? ReceiptRetainUntil { get; private set; }

    public static KeyObligation Capture(ModerationRecognitionCaptureItem item, ModerationRecognitionPrivacySource source,
        string materialReceipt)
    {
        item.ValidatePersistedState();
        source.ValidatePersistedState();
        if (item.SourceId != source.Id || item.SourceRevision != source.Revision
            || item.SourceGeneration != source.WriterGeneration || item.TrustDomainId != source.TrustDomainId)
            throw ModerationRecognitionKeyLifecycle.Unavailable();
        var result = new KeyObligation
        {
            Id = item.Id, IntentId = item.IntentId, TrustDomainId = item.TrustDomainId, SourceId = source.Id,
            KeyId = item.KeyId, KeyGeneration = item.KeyGeneration, VerificationTag = item.KeyVerificationTag,
            CaptureMaterialReceipt = item.ComputeMaterialReceipt(),
            MaterialReceipt = materialReceipt,
            OriginalCapturedAt = item.OriginalCapturedAt, OriginalRetainUntil = item.RetainUntil,
            BackupHorizonTicks = source.MaximumBackupHorizonTicks, BackupSafetyMarginTicks = source.BackupSafetyMarginTicks,
            ReceiptRetentionTicks = source.Retention.Single(row =>
                row.RecordClass == ModerationRecognitionPrivacyRecordClass.DispositionAndReplayReceipt).MaximumRetentionTicks,
            ReadableUntil = item.RetainUntil.AddTicks(checked(source.MaximumBackupHorizonTicks + source.BackupSafetyMarginTicks))
        };
        result.ValidatePersistedState();
        return result;
    }

    public bool Matches(ModerationRecognitionCaptureItem item) => Id == item.Id && IntentId == item.IntentId
        && TrustDomainId == item.TrustDomainId && SourceId == item.SourceId && KeyId == item.KeyId
        && KeyGeneration == item.KeyGeneration && VerificationTag == item.KeyVerificationTag
        && OriginalCapturedAt == item.OriginalCapturedAt && OriginalRetainUntil == item.RetainUntil
        && CaptureMaterialReceipt == item.ComputeMaterialReceipt();

    public void BindAuthority(long sequence)
    {
        if (sequence < 1 || AuthoritySequence is { } original && original != sequence)
            throw ModerationRecognitionKeyLifecycle.Unavailable();
        AuthoritySequence = sequence;
    }

    public void Prune(ModerationRecognitionCaptureItem item, DateTimeOffset now)
    {
        ValidatePersistedState();
        if (!Matches(item) || !ModerationRecognitionKeyLifecycle.Finite(now)
            || now < OriginalCapturedAt || AuthoritySequence is null
            || (item.DroppedAt is null || item.DroppedAt > now)
                && now < OriginalRetainUntil && (item.EndsAt is null || now < item.EndsAt))
            throw ModerationRecognitionKeyLifecycle.Unavailable();
        if (MaterialPrunedAt is null)
        {
            var until = now.AddTicks(ReceiptRetentionTicks);
            MaterialPrunedAt = now;
            ReceiptRetainUntil = until;
        }
    }

    public bool CanRetire(DateTimeOffset now, long verifiedErasureHead)
    {
        ValidatePersistedState();
        return now >= ReadableUntil && AuthoritySequence is { } sequence && sequence <= verifiedErasureHead;
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, IntentId, TrustDomainId, SourceId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        PrivacyIdentityFingerprint.ValidateKeyId(KeyId);
        PrivacyIdentityFingerprint.ValidateDigest(VerificationTag);
        PrivacyIdentityFingerprint.ValidateDigest(CaptureMaterialReceipt);
        PrivacyIdentityFingerprint.ValidateDigest(MaterialReceipt);
        if (KeyGeneration is < 1 or long.MaxValue || !ModerationRecognitionKeyLifecycle.Finite(OriginalCapturedAt)
            || !ModerationRecognitionKeyLifecycle.Finite(OriginalRetainUntil) || OriginalRetainUntil <= OriginalCapturedAt
            || !ModerationRecognitionKeyLifecycle.Finite(ReadableUntil) || ReadableUntil <= OriginalRetainUntil
            || BackupHorizonTicks <= 0 || BackupSafetyMarginTicks <= 0
            || BackupHorizonTicks > DateTimeOffset.MaxValue.Ticks - OriginalRetainUntil.Ticks
            || BackupSafetyMarginTicks > DateTimeOffset.MaxValue.Ticks - OriginalRetainUntil.Ticks - BackupHorizonTicks
            || ReadableUntil.Ticks != OriginalRetainUntil.Ticks + BackupHorizonTicks + BackupSafetyMarginTicks
            || ReceiptRetentionTicks is <= 0 or long.MaxValue
            || AuthoritySequence is <= 0
            || (MaterialPrunedAt is null) != (ReceiptRetainUntil is null)
            || MaterialPrunedAt is { } pruned && (!ModerationRecognitionKeyLifecycle.Finite(pruned)
                || pruned < OriginalCapturedAt || AuthoritySequence is null
                || ReceiptRetainUntil is not { } until || !ModerationRecognitionKeyLifecycle.Finite(until)
                || ReceiptRetentionTicks > DateTimeOffset.MaxValue.Ticks - pruned.Ticks
                || until.Ticks != pruned.Ticks + ReceiptRetentionTicks))
            throw ModerationRecognitionKeyLifecycle.Unavailable();
    }
    public override string ToString() => nameof(KeyObligation);
}
