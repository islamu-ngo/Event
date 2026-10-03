using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Architecture.Tests.Privacy;

public sealed class PrivacyErasureContractArchitectureTests
{
    [Test]
    public async Task PrivacyErasureContracts_ExposeOnlyTypedBoundedFields()
    {
        await Assert.That(Enum.GetValues<PrivacyErasureSubjectKind>())
            .IsEquivalentTo([PrivacyErasureSubjectKind.User]);

        string[] forbidden =
        [
            "LocationIds", "OwnerUserId", "Table", "Column", "Sql", "Json", "Metadata", "Instructions"
        ];
        Type[] contracts =
        [
            typeof(PrivacyErasureIntent),
            typeof(PrivacyErasureReplayCheckpoint),
            typeof(PrivacyErasureRequest),
            typeof(PrivacyErasureIdentityFence),
            typeof(PrivacyIdentityFingerprint)
        ];

        await Assert.That(contracts.SelectMany(type => type.GetProperties())
            .Any(property => forbidden.Contains(property.Name, StringComparer.OrdinalIgnoreCase)))
            .IsFalse();
        await Assert.That(contracts.SelectMany(type => type.GetProperties()
                .Where(property => property.PropertyType == typeof(string))
                .Select(property => (type, property.Name))))
            .IsEquivalentTo([
                (typeof(PrivacyErasureRequest), nameof(PrivacyErasureRequest.IdentityKeyId)),
                (typeof(PrivacyErasureRequest), nameof(PrivacyErasureRequest.IdentityKeyVerificationTag)),
                (typeof(PrivacyErasureIdentityFence), nameof(PrivacyErasureIdentityFence.KeyId)),
                (typeof(PrivacyErasureIdentityFence), nameof(PrivacyErasureIdentityFence.Fingerprint)),
                (typeof(PrivacyIdentityFingerprint), nameof(PrivacyIdentityFingerprint.KeyId)),
                (typeof(PrivacyIdentityFingerprint), nameof(PrivacyIdentityFingerprint.Fingerprint))
            ]);
        await Assert.That(typeof(PrivacyErasureRequest).GetProperty(nameof(PrivacyErasureRequest.IdentityFences))!.PropertyType)
            .IsEqualTo(typeof(IReadOnlyList<PrivacyIdentityFingerprint>));
        await Assert.That(typeof(PrivacyErasureIntent).GetProperty(nameof(PrivacyErasureIntent.IdentityFences))!.PropertyType)
            .IsEqualTo(typeof(IReadOnlyCollection<PrivacyErasureIdentityFence>));
    }

    [Test]
    [Arguments("", false)]
    [Arguments("key with spaces", false)]
    [Arguments("key@example.test", false)]
    [Arguments("raw-subject", true)]
    [Arguments("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", true)]
    public async Task IdentityMetadataRejectsUnboundedOrNonDigestValues(string invalid, bool isDigest)
    {
        string keyId = isDigest ? "key-1" : invalid;
        string digest = isDigest ? invalid : new string('a', 64);

        await Assert.That(() => new PrivacyIdentityFingerprint(AuthenticationProviderKind.Keycloak, keyId, digest))
            .Throws<ArgumentException>();
        await Assert.That(() => new PrivacyErasureRequest(
            Guid.CreateVersion7(), PrivacyErasureSubjectKind.User, Guid.CreateVersion7(),
            PrivacyErasureReasonCode.AccountDeletion, 1, [], keyId, digest))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task IdentityMetadataEnforcesExactBoundsAndMatchingKey()
    {
        string keyId = new('k', 64);
        string digest = new('a', 64);
        var fingerprint = new PrivacyIdentityFingerprint(AuthenticationProviderKind.Keycloak, keyId, digest);
        var request = new PrivacyErasureRequest(
            Guid.CreateVersion7(), PrivacyErasureSubjectKind.User, Guid.CreateVersion7(),
            PrivacyErasureReasonCode.AccountDeletion, 1, [fingerprint], keyId, digest);

        await Assert.That(request.IdentityFences.Single()).IsEqualTo(fingerprint);
        await Assert.That(() => new PrivacyIdentityFingerprint(AuthenticationProviderKind.Keycloak, keyId + "k", digest))
            .Throws<ArgumentException>();
        await Assert.That(() => new PrivacyIdentityFingerprint(AuthenticationProviderKind.Keycloak, keyId, digest + "a"))
            .Throws<ArgumentException>();
        await Assert.That(() => new PrivacyErasureRequest(
            Guid.CreateVersion7(), PrivacyErasureSubjectKind.User, Guid.CreateVersion7(),
            PrivacyErasureReasonCode.AccountDeletion, 1, [], keyId + "k", digest))
            .Throws<ArgumentException>();
        await Assert.That(() => new PrivacyErasureRequest(
            Guid.CreateVersion7(), PrivacyErasureSubjectKind.User, Guid.CreateVersion7(),
            PrivacyErasureReasonCode.AccountDeletion, 1, [], keyId, digest + "a"))
            .Throws<ArgumentException>();
        await Assert.That(() => new PrivacyErasureRequest(
            Guid.CreateVersion7(), PrivacyErasureSubjectKind.User, Guid.CreateVersion7(),
            PrivacyErasureReasonCode.AccountDeletion, 1, [fingerprint], "other-key", digest))
            .Throws<ArgumentException>();
    }
}
