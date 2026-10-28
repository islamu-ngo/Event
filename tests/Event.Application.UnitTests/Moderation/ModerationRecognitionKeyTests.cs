using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using ISLAMU.Moderation.Domain.Policies;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationRecognitionKeyTests
{
    private static readonly Guid Trust = Guid.Parse("019a8f4a-0000-7000-8000-000000000001");
    private const ModerationReferenceRecognition Action = ModerationReferenceRecognition.SeparatelyQualifiedFinalAction;

    [Test]
    public async Task Canonical_version_one_uses_UTF8_byte_lengths_and_exact_order_with_an_independent_HMAC_oracle()
    {
        var material = RandomNumberGenerator.GetBytes(32);
        using var key = new ModerationRecognitionKey(Trust, "key-one", 7, material);
        // Independently specified wire bytes, not the production framing loop.
        var wire = Encoding.UTF8.GetBytes(
            "\0\0\0\u0028ISLAMU.Moderation.Recognition.Account.v1" +
            "\0\0\0\u000cfinal-action" +
            "\0\0\0\u0024019a8f4a-0000-7000-8000-000000000001" +
            "\0\0\0\u0007key-one" +
            "\0\0\0\u00017" +
            "\0\0\0\u00011" +
            "\0\0\0\u0004a:\u00e9");
        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(material, wire));
        CryptographicOperations.ZeroMemory(material);
        CryptographicOperations.ZeroMemory(wire);
        var account = new ProviderAccountKey(AuthenticationProviderKind.Keycloak, "a:\u00e9");
        var token = key.Compute(account, Action);
        await Assert.That(token.Digest).IsEqualTo(expected);
        await Assert.That(token.SerializationVersion).IsEqualTo(1);
        await Assert.That(token.KeyGeneration).IsEqualTo(7);
        await Assert.That(token.Digest).IsNotEqualTo(key.VerificationTag);
    }

    [Test]
    [Arguments("purpose")]
    [Arguments("trust")]
    [Arguments("key-id")]
    [Arguments("generation")]
    [Arguments("provider")]
    [Arguments("material")]
    public async Task Changed_purpose_trust_key_generation_or_provider_cannot_reuse_a_matching_token(string change)
    {
        var material = RandomNumberGenerator.GetBytes(32);
        using var first = new ModerationRecognitionKey(Trust, "key-one", 7, material);
        var otherMaterial = change == "material" ? RandomNumberGenerator.GetBytes(32) : material.ToArray();
        using var other = new ModerationRecognitionKey(change == "trust" ? Guid.CreateVersion7() : Trust,
            change == "key-id" ? "key-two" : "key-one", change == "generation" ? 8 : 7, otherMaterial);
        CryptographicOperations.ZeroMemory(material);
        CryptographicOperations.ZeroMemory(otherMaterial);
        var account = new ProviderAccountKey(AuthenticationProviderKind.Keycloak, "opaque:subject");
        var changedAccount = change == "provider"
            ? new ProviderAccountKey(AuthenticationProviderKind.Google, account.Value) : account;
        var changedPurpose = change == "purpose" ? ModerationReferenceRecognition.SeparatelyQualifiedFinalEnrollment : Action;
        await Assert.That(first.Compute(account, Action).Digest)
            .IsNotEqualTo(other.Compute(changedAccount, changedPurpose).Digest);
    }

    [Test]
    public async Task Trusted_OIDC_canonicalization_preserves_exact_subject_and_issuer_path_semantics()
    {
        using var key = CreateKey();
        var account = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey("https://ID.EXAMPLE/Realm/", "Subject");
        var equivalent = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey("https://id.example/Realm", "Subject");
        var changedSubject = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey("https://id.example/Realm", "subject");
        var changedIssuerPath = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey("https://id.example/realm", "Subject");
        var splitIssuer = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey("https://id.example/a:b", "c");
        var splitSubject = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey("https://id.example/a", "b:c");
        await Assert.That(key.Compute(account, Action)).IsEqualTo(key.Compute(equivalent, Action));
        await Assert.That(key.Compute(account, Action).Digest).IsNotEqualTo(key.Compute(changedSubject, Action).Digest);
        await Assert.That(key.Compute(account, Action).Digest).IsNotEqualTo(key.Compute(changedIssuerPath, Action).Digest);
        await Assert.That(key.Compute(splitIssuer, Action).Digest).IsNotEqualTo(key.Compute(splitSubject, Action).Digest);
    }

    [Test]
    [Arguments("high")]
    [Arguments("low")]
    public async Task Invalid_Unicode_never_falls_back_to_a_replacement_identity(string surrogate)
    {
        using var key = CreateKey();
        var account = new ProviderAccountKey(AuthenticationProviderKind.Keycloak,
            surrogate == "high" ? "\ud800" : "\udfff");
        await Assert.That(() => key.Compute(account, Action)).Throws<EncoderFallbackException>();
        await Assert.That(key.Compute(new ProviderAccountKey(AuthenticationProviderKind.Keycloak, "valid"), Action).Digest.Length)
            .IsEqualTo(64);
    }

    [Test]
    public async Task Caller_key_buffer_mutation_and_disposal_do_not_change_or_reuse_the_owned_key()
    {
        var material = RandomNumberGenerator.GetBytes(32);
        using var key = new ModerationRecognitionKey(Trust, "key-one", 7, material);
        using var restarted = new ModerationRecognitionKey(Trust, "key-one", 7, material);
        CryptographicOperations.ZeroMemory(material);
        var account = new ProviderAccountKey(AuthenticationProviderKind.Atproto, "did:plc:opaque");
        await Assert.That(key.Compute(account, Action)).IsEqualTo(restarted.Compute(account, Action));
        key.Dispose();
        await Assert.That(() => key.Compute(account, Action)).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task Retained_token_roundtrip_contains_no_provider_identifier_or_ordinary_identity_fingerprint()
    {
        using var key = CreateKey();
        var account = new ProviderAccountKey(AuthenticationProviderKind.Atproto, "did:plc:opaque");
        var token = key.Compute(account, Action);
        var json = JsonSerializer.Serialize(token);
        await Assert.That(JsonSerializer.Deserialize<ModerationRecognitionToken>(json)).IsEqualTo(token);
        await Assert.That(json.Contains(account.Value, StringComparison.Ordinal)).IsFalse();
        await Assert.That(token.ToString().Contains(token.Digest, StringComparison.Ordinal)).IsFalse();
        await Assert.That(key.ToString().Contains(key.VerificationTag, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Unsupported_provider_or_unqualified_purpose_cannot_produce_retained_material(bool unsupportedProvider)
    {
        using var key = CreateKey();
        var account = new ProviderAccountKey(unsupportedProvider
            ? AuthenticationProviderKind.Development : AuthenticationProviderKind.Keycloak, "opaque");
        await Assert.That(() => key.Compute(account, unsupportedProvider ? Action : ModerationReferenceRecognition.Never))
            .Throws<ArgumentException>();
    }

    private static ModerationRecognitionKey CreateKey()
    {
        var material = RandomNumberGenerator.GetBytes(32);
        try { return new(Trust, "key-one", 7, material); }
        finally { CryptographicOperations.ZeroMemory(material); }
    }
}
