namespace Explore.Domain;

/// <summary>
/// Minimal exact replay disposition after a whole computed set reaches its original ceiling.
/// No digest tokens, subject, binding, accusation or secret bytes survive in this row.
/// It proves expired material removal, not current recognition or permission.
/// </summary>
public sealed class ModerationRecognitionKeyLifecyclePurge
{
    private ModerationRecognitionKeyLifecyclePurge() { }
    public Guid IntentId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public string MaterialReceipt { get; private set; } = string.Empty;
    public int MaterialCount { get; private set; }
    public long AuthoritySequence { get; private set; }
    public DateTimeOffset PurgedAt { get; private set; }
    public DateTimeOffset BackupObligationUntil { get; private set; }
    public DateTimeOffset RetainUntil { get; private set; }

    public static ModerationRecognitionKeyLifecyclePurge Complete(ModerationRecognitionPending pending,
        IReadOnlyCollection<KeyObligation> obligations, DateTimeOffset now)
    {
        pending.ValidatePersistedState();
        if (!pending.HasCompleteMaterial || pending.AuthoritySequence is not { } sequence
            || pending.State != ModerationRecognitionPendingState.AuthorityBound
            || pending.Failure != ModerationRecognitionPendingFailure.None
            || obligations.Count != pending.MaterialCount || obligations.Count == 0
            || obligations.Any(row => row.IntentId != pending.IntentId || row.AuthoritySequence != sequence
                || row.MaterialReceipt != pending.MaterialReceipt
                || row.MaterialPrunedAt != now || row.ReceiptRetainUntil is null
                || !pending.Material.Any(row.Matches)))
            throw ModerationRecognitionKeyLifecycle.Unavailable();
        var result = new ModerationRecognitionKeyLifecyclePurge
        {
            IntentId = pending.IntentId,
            TrustDomainId = obligations.First().TrustDomainId,
            MaterialReceipt = pending.MaterialReceipt!,
            MaterialCount = pending.MaterialCount,
            AuthoritySequence = sequence,
            PurgedAt = now,
            BackupObligationUntil = obligations.Max(row => row.ReadableUntil),
            RetainUntil = obligations.Min(row => row.ReceiptRetainUntil!.Value)
        };
        result.ValidatePersistedState();
        return result;
    }

    public void ValidatePersistedState()
    {
        ModerationHostBinding.RequireUuidV7(IntentId, nameof(IntentId));
        ModerationHostBinding.RequireUuidV7(TrustDomainId, nameof(TrustDomainId));
        PrivacyIdentityFingerprint.ValidateDigest(MaterialReceipt);
        if (MaterialCount < 1 || AuthoritySequence < 1 || !ModerationRecognitionKeyLifecycle.Finite(PurgedAt)
            || !ModerationRecognitionKeyLifecycle.Finite(BackupObligationUntil)
            || !ModerationRecognitionKeyLifecycle.Finite(RetainUntil) || RetainUntil <= PurgedAt)
            throw ModerationRecognitionKeyLifecycle.Unavailable();
    }
    public override string ToString() => nameof(ModerationRecognitionKeyLifecyclePurge);
}
