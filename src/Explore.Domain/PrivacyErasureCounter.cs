namespace Explore.Domain;

public sealed class PrivacyErasureCounter
{
    private PrivacyErasureCounter()
    {
    }

    public bool Singleton { get; private set; }
    public Guid StoreId { get; private set; }
    public long Revision { get; private set; }
    public long RecoveryGeneration { get; private set; }
    public long LastSequence { get; private set; }
    public long RetainedFloorSequence { get; private set; }
    public string? IdentityKeyId { get; private set; }
    public string? IdentityKeyVerificationTag { get; private set; }

    public void BindIdentityKey(string keyId, string verificationTag)
    {
        PrivacyIdentityFingerprint.ValidateKeyId(keyId);
        PrivacyIdentityFingerprint.ValidateDigest(verificationTag);
        if (IdentityKeyId is not null
            && (IdentityKeyId != keyId || IdentityKeyVerificationTag != verificationTag))
            throw new InvalidOperationException("privacy_identity_fence_key_mismatch");
        if (IdentityKeyId == keyId)
            return;
        AdvanceRevision();
        IdentityKeyId = keyId;
        IdentityKeyVerificationTag = verificationTag;
    }

    public static PrivacyErasureCounter Start() => new()
    {
        Singleton = true,
        StoreId = Guid.CreateVersion7(),
        Revision = 1,
        RecoveryGeneration = 1,
        LastSequence = 0,
        RetainedFloorSequence = 0
    };

    public long AllocateNext()
    {
        if (LastSequence == long.MaxValue)
        {
            throw new InvalidOperationException("The erasure-ledger sequence is exhausted.");
        }

        AdvanceRevision();
        return ++LastSequence;
    }

    public void AdvanceTo(long sequence)
    {
        if (LastSequence == long.MaxValue || sequence != LastSequence + 1)
        {
            throw new InvalidOperationException("Mirrored erasure facts must advance the ledger by one.");
        }

        AdvanceRevision();
        LastSequence = sequence;
    }

    public void AdvanceRetainedFloorTo(long sequence)
    {
        if (sequence < RetainedFloorSequence || sequence > LastSequence)
        {
            throw new InvalidOperationException("The retained floor must advance monotonically within the high-water mark.");
        }

        if (sequence != RetainedFloorSequence)
        {
            AdvanceRevision();
            RetainedFloorSequence = sequence;
        }
    }

    /// <summary>Records a nonempty compaction, including removal of a held row at the existing floor.</summary>
    internal void RecordCompaction(long retainedFloorSequence)
    {
        if (retainedFloorSequence < RetainedFloorSequence || retainedFloorSequence > LastSequence)
            throw new InvalidOperationException("The retained floor must advance monotonically within the high-water mark.");
        AdvanceRevision();
        RetainedFloorSequence = retainedFloorSequence;
    }

    /// <summary>
    /// Reserved for a privileged recovery owner holding the native writer gate.
    /// No runtime port exposes this transition until that owner exists. Advancing
    /// generation invalidates prior coordinates; it never certifies a completed recovery.
    /// </summary>
    internal void BeginRecovery(PrivacyErasureRecoveryState expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (GetRecoveryState() != expected)
            throw new InvalidOperationException("privacy_erasure_recovery_state_changed");
        if (RecoveryGeneration == long.MaxValue)
            throw new InvalidOperationException("privacy_erasure_recovery_generation_exhausted");
        AdvanceRevision();
        RecoveryGeneration++;
    }

    private void AdvanceRevision()
    {
        if (Revision == long.MaxValue)
            throw new InvalidOperationException("privacy_erasure_recovery_revision_exhausted");
        Revision++;
    }

    public PrivacyErasureRecoveryState GetRecoveryState() =>
        new(StoreId, Revision, RecoveryGeneration, GetState());

    public PrivacyErasureAuthorityState GetState() =>
        new(LastSequence, RetainedFloorSequence, IdentityKeyId, IdentityKeyVerificationTag);
}
