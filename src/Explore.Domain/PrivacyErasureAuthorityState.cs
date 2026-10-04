namespace Explore.Domain;

public sealed record PrivacyErasureAuthorityState
{
    public PrivacyErasureAuthorityState(long highWaterSequence, long retainedFloorSequence,
        string? identityKeyId = null, string? identityKeyVerificationTag = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(highWaterSequence);
        ArgumentOutOfRangeException.ThrowIfNegative(retainedFloorSequence);
        if (retainedFloorSequence > highWaterSequence)
        {
            throw new ArgumentException("The retained floor cannot exceed the authority high-water mark.");
        }

        HighWaterSequence = highWaterSequence;
        RetainedFloorSequence = retainedFloorSequence;
        if (identityKeyId is not null || identityKeyVerificationTag is not null)
        {
            PrivacyIdentityFingerprint.ValidateKeyId(identityKeyId!);
            PrivacyIdentityFingerprint.ValidateDigest(identityKeyVerificationTag!);
        }
        IdentityKeyId = identityKeyId;
        IdentityKeyVerificationTag = identityKeyVerificationTag;
    }

    public long HighWaterSequence { get; }
    public long RetainedFloorSequence { get; }
    public string? IdentityKeyId { get; }
    public string? IdentityKeyVerificationTag { get; }
}
