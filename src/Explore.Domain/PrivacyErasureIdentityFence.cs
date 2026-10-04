using Explore.Domain.Enums;

namespace Explore.Domain;

public sealed class PrivacyErasureIdentityFence
{
    private PrivacyErasureIdentityFence() { }

    public long AuthoritySequence { get; private set; }
    public AuthenticationProviderKind IdentityKind { get; private set; }
    public string KeyId { get; private set; } = null!;
    public string Fingerprint { get; private set; } = null!;
    public DateTime RetentionExpiresAtUtc { get; private set; }

    internal static PrivacyErasureIdentityFence Capture(
        long authoritySequence, PrivacyIdentityFingerprint fingerprint, DateTime retentionExpiresAtUtc) =>
        new()
        {
            AuthoritySequence = authoritySequence,
            IdentityKind = fingerprint.IdentityKind,
            KeyId = fingerprint.KeyId,
            Fingerprint = fingerprint.Fingerprint,
            RetentionExpiresAtUtc = retentionExpiresAtUtc
        };

    public PrivacyIdentityFingerprint GetFingerprint() => new(IdentityKind, KeyId, Fingerprint);
}
