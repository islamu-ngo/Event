using System.Security.Cryptography;
using Explore.Application.Authentication;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Services;

public sealed class PrivacyIdentityFingerprintTests
{
    [Test]
    public async Task RetainedRequestRoundTripPreservesFingerprintsAndSnapshotsInput()
    {
        using var key = new PrivacyIdentityFenceKey("key-1", RandomNumberGenerator.GetBytes(32));
        var fingerprint = key.Fingerprint(new ProviderAccountKey(AuthenticationProviderKind.Atproto, "did:plc:opaque"));
        var input = new List<Explore.Domain.PrivacyIdentityFingerprint> { fingerprint };
        var request = new PrivacyErasureRequest(Guid.CreateVersion7(), Explore.Domain.PrivacyErasureSubjectKind.User,
            Guid.CreateVersion7(), Explore.Domain.PrivacyErasureReasonCode.AccountDeletion, 1,
            input, key.KeyId, key.VerificationTag);
        input.Clear();
        var restored = System.Text.Json.JsonSerializer.Deserialize<PrivacyErasureRequest>(
            System.Text.Json.JsonSerializer.Serialize(request))!;

        await Assert.That(restored.IdentityFences.Single()).IsEqualTo(fingerprint);
        await Assert.That(restored.IdentityKeyVerificationTag).IsEqualTo(key.VerificationTag);
    }

    [Test]
    public async Task MalformedUnicodeCannotCollapseDistinctOpaqueSubjects()
    {
        using var key = new PrivacyIdentityFenceKey("key-1", RandomNumberGenerator.GetBytes(32));
        await Assert.That(() => key.Fingerprint(new ProviderAccountKey(
            AuthenticationProviderKind.Keycloak, "\ud800")))
            .Throws<System.Text.EncoderFallbackException>();
    }

    [Test]
    public async Task FingerprintPreservesCanonicalIssuerAndOpaqueSubjectEquivalence()
    {
        using var key = new PrivacyIdentityFenceKey("key-1", RandomNumberGenerator.GetBytes(32));
        ProviderAccountKey first = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            "https://id.example/realm/", "Subject");
        ProviderAccountKey equivalent = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            "https://id.example/realm", "Subject");
        ProviderAccountKey different = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            "https://id.example/realm", "subject");

        await Assert.That(key.Fingerprint(first)).IsEqualTo(key.Fingerprint(equivalent));
        await Assert.That(key.Fingerprint(first)).IsNotEqualTo(key.Fingerprint(different));
        await Assert.That(key.Fingerprint(first).Fingerprint).IsNotEqualTo(key.VerificationTag);
    }

    [Test]
    public async Task RestartUsesProvisionedKeyAndProviderKindIsPurposeBound()
    {
        byte[] material = RandomNumberGenerator.GetBytes(32);
        using var first = new PrivacyIdentityFenceKey("key-1", material);
        using var restarted = new PrivacyIdentityFenceKey("key-1", material);
        using var wrong = new PrivacyIdentityFenceKey("key-1", RandomNumberGenerator.GetBytes(32));
        var account = new ProviderAccountKey(AuthenticationProviderKind.Keycloak, "opaque:Subject");

        await Assert.That(first.Fingerprint(account)).IsEqualTo(restarted.Fingerprint(account));
        await Assert.That(first.Fingerprint(account)).IsNotEqualTo(wrong.Fingerprint(account));
        await Assert.That(first.Fingerprint(account)).IsNotEqualTo(first.Fingerprint(
            new ProviderAccountKey(AuthenticationProviderKind.Google, account.Value)));
        CryptographicOperations.ZeroMemory(material);
    }
}
