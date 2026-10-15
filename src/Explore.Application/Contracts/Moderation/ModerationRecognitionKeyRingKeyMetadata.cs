namespace Explore.Application.Contracts.Moderation;

/// <summary>
/// Exact value-free identity and material commitment for one readable recognition key.
/// VerificationTag is the committed key primitive's separate purpose-bound HMAC, not an
/// account token. Equal identity/generation with a different tag is material divergence.
/// These facts do not attest durable replica or backup floors.
/// </summary>
public sealed record ModerationRecognitionKeyRingKeyMetadata(
    string KeyId,
    long KeyGeneration,
    bool IsWriting,
    string VerificationTag)
{
    public override string ToString() => nameof(ModerationRecognitionKeyRingKeyMetadata);
}
