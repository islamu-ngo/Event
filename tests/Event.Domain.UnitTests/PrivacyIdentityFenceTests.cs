using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Domain.UnitTests;

public sealed class PrivacyIdentityFenceTests
{
    [Test]
    public async Task LegalHoldChangesAuditIdsButNotIdentityFenceAssociation()
    {
        DateTime now = DateTime.UtcNow;
        var fingerprint = new PrivacyIdentityFingerprint(
            AuthenticationProviderKind.Atproto, "key-1", new string('a', 64));
        PrivacyErasureIntent intent = PrivacyErasureIntent.Record(
            Guid.CreateVersion7(), 7, PrivacyErasureSubjectKind.User, Guid.CreateVersion7(),
            PrivacyErasureReasonCode.AccountDeletion, 1, now, now, now.AddDays(90), [fingerprint]);

        intent.PseudonymizeForLegalHold(Guid.NewGuid(), Guid.NewGuid());

        await Assert.That(intent.IdentityFences.Single().AuthoritySequence).IsEqualTo(7);
        await Assert.That(intent.IdentityFences.Single().Fingerprint).IsEqualTo(fingerprint.Fingerprint);
        await Assert.That(intent.IdentityFences.Single().RetentionExpiresAtUtc).IsEqualTo(now.AddDays(90));
    }

    [Test]
    public async Task DuplicateFingerprintsCannotChangeTheAppendPayload()
    {
        DateTime now = DateTime.UtcNow;
        var fingerprint = new PrivacyIdentityFingerprint(
            AuthenticationProviderKind.Keycloak, "key-1", new string('b', 64));
        PrivacyErasureIntent intent = PrivacyErasureIntent.Record(
            Guid.CreateVersion7(), 1, PrivacyErasureSubjectKind.User, Guid.CreateVersion7(),
            PrivacyErasureReasonCode.AccountDeletion, 1, now, now, now.AddDays(90),
            [fingerprint, fingerprint]);

        await Assert.That(intent.IdentityFences.Count).IsEqualTo(1);
        await Assert.That(intent.HasSameIdentityFences([fingerprint])).IsTrue();
        await Assert.That(intent.HasSameIdentityFences([])).IsFalse();
    }

    [Test]
    public async Task KeyCommitmentCannotBeReplacedByAnotherKeyOrIdentifier()
    {
        PrivacyErasureCounter counter = PrivacyErasureCounter.Start();
        counter.BindIdentityKey("key-1", new string('a', 64));
        counter.BindIdentityKey("key-1", new string('a', 64));

        await Assert.That(() => counter.BindIdentityKey("key-1", new string('b', 64)))
            .Throws<InvalidOperationException>();
        await Assert.That(() => counter.BindIdentityKey("key-2", new string('a', 64)))
            .Throws<InvalidOperationException>();
        await Assert.That(counter.IdentityKeyId).IsEqualTo("key-1");
    }

    [Test]
    public async Task FenceRejectsOpenKindsAndMalformedDigests()
    {
        await Assert.That(() => new PrivacyIdentityFingerprint(
            (AuthenticationProviderKind)999, "key-1", new string('a', 64)))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new PrivacyIdentityFingerprint(
            AuthenticationProviderKind.Keycloak, "key-1", "raw-subject"))
            .Throws<ArgumentException>();
    }
}
