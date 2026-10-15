using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;
using ISLAMU.Moderation.Domain.Policies;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationRecognitionKeyRingTests
{
    [Test]
    public async Task ExactWritingAndReadableSelectionPreservesNativeComputation()
    {
        using var fixture = new Fixture();
        using var ring = fixture.Parse();
        using var writing = ring.BorrowWritingKey(fixture.TrustDomainId, "current", 2);
        using var readable = ring.BorrowReadableKey(fixture.TrustDomainId, "previous", 1);
        using var expected = new ModerationRecognitionKey(fixture.TrustDomainId, "current", 2, fixture.Current);
        var account = new ProviderAccountKey(AuthenticationProviderKind.Local, Guid.CreateVersion7().ToString("D"));

        await Assert.That(writing.Compute(account, ModerationReferenceRecognition.SeparatelyQualifiedFinalAction))
            .IsEqualTo(expected.Compute(account, ModerationReferenceRecognition.SeparatelyQualifiedFinalAction));
        await Assert.That(writing.VerificationTag).IsEqualTo(expected.VerificationTag);
        await Assert.That(readable.KeyGeneration).IsEqualTo(1L);
        await Assert.That(ring.Keys.Length).IsEqualTo(2);
        await Assert.That(ring.Keys.Single(key => key.IsWriting).VerificationTag).IsEqualTo(writing.VerificationTag);
        await Assert.That(ring.RingGeneration).IsEqualTo(3L);
        await Assert.That(ring.CurrentUntil).IsEqualTo(fixture.Clock.Now.AddMinutes(5));
    }

    [Test]
    [Arguments("version")]
    [Arguments("trustDomainId")]
    [Arguments("ringGeneration")]
    [Arguments("issuedAt")]
    [Arguments("validUntil")]
    [Arguments("keys")]
    public async Task EveryRingFieldIsRequired(string field)
    {
        using var fixture = new Fixture();
        fixture.Payload.Remove(field);
        await Reject(fixture);
    }

    [Test]
    [Arguments("kid")]
    [Arguments("generation")]
    [Arguments("k")]
    [Arguments("status")]
    public async Task EveryKeyFieldIsRequired(string field)
    {
        using var fixture = new Fixture();
        fixture.Payload["keys"]![0]!.AsObject().Remove(field);
        await Reject(fixture);
    }

    [Test]
    [Arguments("version", "2")]
    [Arguments("version", "\"1\"")]
    [Arguments("ringGeneration", "0")]
    [Arguments("ringGeneration", "9223372036854775807")]
    [Arguments("ringGeneration", "1.5")]
    [Arguments("replicasConverged", "true")]
    [Arguments("backupSafe", "true")]
    [Arguments("retirementApproved", "true")]
    public async Task InvalidOrForeignRingMetadataCannotCertifyReadiness(string field, string json)
    {
        using var fixture = new Fixture();
        fixture.Payload[field] = JsonNode.Parse(json);
        await Reject(fixture);
    }

    [Test]
    [Arguments("kid", "\"bad.id\"")]
    [Arguments("kid", "\"\"")]
    [Arguments("generation", "0")]
    [Arguments("generation", "9223372036854775807")]
    [Arguments("generation", "\"2\"")]
    [Arguments("status", "\"retired\"")]
    [Arguments("status", "\"compromised\"")]
    [Arguments("status", "\"ACTIVE\"")]
    [Arguments("readable", "false")]
    public async Task InvalidOrUnreadableKeyCannotEnterSnapshot(string field, string json)
    {
        using var fixture = new Fixture();
        fixture.Payload["keys"]![0]![field] = JsonNode.Parse(json);
        await Reject(fixture);
    }

    [Test]
    public async Task DuplicatePropertiesIdentifiersAndGenerationsAreRejected()
    {
        using var fixture = new Fixture();
        var serialized = fixture.Payload.ToJsonString();
        await Reject(fixture, serialized.Replace("\"version\":1", "\"version\":1,\"version\":1", StringComparison.Ordinal));
        await Reject(fixture, serialized.Replace("\"kid\":\"current\"", "\"kid\":\"current\",\"kid\":\"current\"", StringComparison.Ordinal));
        fixture.Payload["keys"]![1]!["kid"] = "current";
        await Reject(fixture);
        fixture.Payload["keys"]![1]!["kid"] = "previous";
        fixture.Payload["keys"]![1]!["generation"] = 2;
        await Reject(fixture);
    }

    [Test]
    public async Task MissingOrAmbiguousWritingKeyRejectsEntireRing()
    {
        using var fixture = new Fixture();
        fixture.Payload["keys"]![0]!["status"] = "readable";
        await Reject(fixture);
        fixture.Payload["keys"]![0]!["status"] = "active";
        fixture.Payload["keys"]![1]!["status"] = "active";
        await Reject(fixture);
    }

    [Test]
    public async Task ForeignTrustAndNonCanonicalTrustAreRejected()
    {
        using var fixture = new Fixture();
        fixture.Payload["trustDomainId"] = Guid.CreateVersion7().ToString("D");
        await Reject(fixture);
        fixture.Payload["trustDomainId"] = fixture.TrustDomainId.ToString("N");
        await Reject(fixture);
    }

    [Test]
    public async Task MaterialMustBeCanonicalUnpaddedBase64UrlOfExactlyThirtyTwoBytes()
    {
        using var fixture = new Fixture();
        foreach (var length in new[] { 0, 31, 33 })
        {
            var bytes = RandomNumberGenerator.GetBytes(length);
            try
            {
                fixture.Payload["keys"]![0]!["k"] = Fixture.Encode(bytes);
                await Reject(fixture);
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        var canonical = Fixture.Encode(fixture.Current);
        fixture.Payload["keys"]![0]!["k"] = canonical + "=";
        await Reject(fixture);
        fixture.Payload["keys"]![0]!["k"] = " " + canonical;
        await Reject(fixture);
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        var last = alphabet.IndexOf(canonical[^1]);
        fixture.Payload["keys"]![0]!["k"] = canonical[..^1] + alphabet[last + 1];
        await Reject(fixture);
    }

    [Test]
    public async Task MalformedEmptyOversizedAndExcessiveRingsFailClosed()
    {
        using var fixture = new Fixture();
        foreach (var payload in new[] { "", "null", "[]", "{", new string(' ', 65537) })
            await Reject(fixture, payload);
        fixture.Payload["keys"] = new JsonArray();
        await Reject(fixture);
        fixture.Payload["keys"] = new JsonArray(Enumerable.Range(0, 17)
            .Select(_ => Fixture.Key("current", 2, fixture.Current, "active")).ToArray());
        await Reject(fixture);
    }

    [Test]
    public async Task ExpiredFutureAndInfiniteWindowsAreRejected()
    {
        using var fixture = new Fixture();
        fixture.Payload["validUntil"] = Fixture.Timestamp(fixture.Clock.Now);
        await Reject(fixture);
        fixture.Payload["validUntil"] = Fixture.Timestamp(DateTimeOffset.MaxValue);
        await Reject(fixture);
        fixture.Payload["validUntil"] = Fixture.Timestamp(fixture.Clock.Now.AddMinutes(10));
        fixture.Payload["issuedAt"] = Fixture.Timestamp(fixture.Clock.Now.AddSeconds(1));
        await Reject(fixture);
        fixture.Payload["issuedAt"] = "2026-10-10T12:00:00+01:00";
        await Reject(fixture);
    }

    [Test]
    public async Task CurrentnessEndsAtEarliestRingOrAuthorityDeadlineAndRejectsClockRollback()
    {
        using var fixture = new Fixture();
        fixture.Payload["validUntil"] = Fixture.Timestamp(fixture.Clock.Now.AddMinutes(2));
        using var ring = fixture.Parse();
        await Assert.That(ring.CurrentUntil).IsEqualTo(fixture.Clock.Now.AddMinutes(2));
        fixture.Clock.Now = ring.CurrentUntil;
        await Assert.That(() => ring.BorrowWritingKey(fixture.TrustDomainId, "current", 2))
            .Throws<InvalidOperationException>();
        fixture.Clock.Now = ring.ObservedAt.AddTicks(-1);
        await Assert.That(() => ring.BorrowReadableKey(fixture.TrustDomainId, "previous", 1))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AlreadyStaleOrFutureAuthorityObservationCannotRefreshSnapshot()
    {
        using var fixture = new Fixture();
        await Reject(fixture, resolvedAt: fixture.Clock.Now.AddMinutes(-5));
        await Reject(fixture, resolvedAt: fixture.Clock.Now.AddTicks(1));
        fixture.Payload["issuedAt"] = Fixture.Timestamp(fixture.Clock.Now.AddMinutes(-10));
        using var ring = fixture.Parse(resolvedAt: fixture.Clock.Now.AddMinutes(-4));
        await Assert.That(ring.CurrentUntil).IsEqualTo(fixture.Clock.Now.AddMinutes(1));
    }

    [Test]
    public async Task SelectionCannotSubstituteTrustIdentityGenerationOrReadOnlyKey()
    {
        using var fixture = new Fixture();
        using var ring = fixture.Parse();
        await Assert.That(() => ring.BorrowWritingKey(Guid.CreateVersion7(), "current", 2)).Throws<InvalidOperationException>();
        await Assert.That(() => ring.BorrowWritingKey(fixture.TrustDomainId, "Current", 2)).Throws<InvalidOperationException>();
        await Assert.That(() => ring.BorrowWritingKey(fixture.TrustDomainId, "current", 1)).Throws<InvalidOperationException>();
        await Assert.That(() => ring.BorrowWritingKey(fixture.TrustDomainId, "previous", 1)).Throws<InvalidOperationException>();
        await Assert.That(() => ring.BorrowReadableKey(fixture.TrustDomainId, "absent", 3)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task SameMetadataWithDifferentMaterialHasDifferentVerificationTag()
    {
        using var fixture = new Fixture();
        using var first = fixture.Parse();
        var replacement = RandomNumberGenerator.GetBytes(32);
        try
        {
            fixture.Payload["keys"]![0]!["k"] = Fixture.Encode(replacement);
            using var second = fixture.Parse();
            await Assert.That(second.Keys[0].KeyId).IsEqualTo(first.Keys[0].KeyId);
            await Assert.That(second.Keys[0].KeyGeneration).IsEqualTo(first.Keys[0].KeyGeneration);
            await Assert.That(second.Keys[0].VerificationTag).IsNotEqualTo(first.Keys[0].VerificationTag);
        }
        finally { CryptographicOperations.ZeroMemory(replacement); }
    }

    [Test]
    public async Task SnapshotAndBorrowedKeysOwnIndependentDisposableMaterial()
    {
        using var fixture = new Fixture();
        using var ring = fixture.Parse();
        using var first = ring.BorrowWritingKey(fixture.TrustDomainId, "current", 2);
        using var second = ring.BorrowWritingKey(fixture.TrustDomainId, "current", 2);
        var account = new ProviderAccountKey(AuthenticationProviderKind.Local, Guid.CreateVersion7().ToString("D"));
        var expected = second.Compute(account, ModerationReferenceRecognition.SeparatelyQualifiedFinalAction);
        CryptographicOperations.ZeroMemory(fixture.Current);
        first.Dispose();
        await Assert.That(() => first.Compute(account, ModerationReferenceRecognition.SeparatelyQualifiedFinalAction))
            .Throws<ObjectDisposedException>();
        ring.Dispose();
        ring.Dispose();
        await Assert.That(() => ring.BorrowReadableKey(fixture.TrustDomainId, "current", 2)).Throws<ObjectDisposedException>();
        await Assert.That(second.Compute(account, ModerationReferenceRecognition.SeparatelyQualifiedFinalAction)).IsEqualTo(expected);
    }

    [Test]
    public async Task ProvenanceMustBeInstanceScopedAndUsesOnlyApprovedSourceCategories()
    {
        using var fixture = new Fixture();
        await Assert.That(() => ModerationRecognitionKeyRing.Parse(
            fixture.Secret() with { Scope = SecretScope.Tenant, ScopeId = Guid.CreateVersion7() },
            fixture.TrustDomainId, fixture.Clock)).Throws<InvalidOperationException>();
        await Assert.That(() => ModerationRecognitionKeyRing.Parse(
            fixture.Secret() with { SettingKey = "foreign", Source = (SecretSourceType)int.MaxValue },
            fixture.TrustDomainId, fixture.Clock)).Throws<InvalidOperationException>();
        using var ring = fixture.Parse();
        await Assert.That(ring.BindingRevision).IsEqualTo(fixture.Revision);
        await Assert.That(ring.ToString()).IsEqualTo(nameof(ModerationRecognitionKeyRing));
        await Assert.That(ring.Keys[0].ToString()).IsEqualTo(nameof(ModerationRecognitionKeyRingKeyMetadata));
    }

    private static async Task Reject(Fixture fixture, string? payload = null, DateTimeOffset? resolvedAt = null)
    {
        await Assert.That(() => fixture.Parse(payload, resolvedAt)).Throws<InvalidOperationException>()
            .WithMessage("moderation_recognition_ring_invalid");
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Fixture : IDisposable
    {
        public Guid TrustDomainId { get; } = Guid.CreateVersion7();
        public string Revision { get; } = Guid.CreateVersion7().ToString("N");
        public byte[] Current { get; } = RandomNumberGenerator.GetBytes(32);
        private byte[] Previous { get; } = RandomNumberGenerator.GetBytes(32);
        public Clock Clock { get; } = new();
        public JsonObject Payload { get; }

        public Fixture()
        {
            Payload = new JsonObject
            {
                ["version"] = 1, ["trustDomainId"] = TrustDomainId.ToString("D"), ["ringGeneration"] = 3,
                ["issuedAt"] = Timestamp(Clock.Now.AddMinutes(-1)),
                ["validUntil"] = Timestamp(Clock.Now.AddMinutes(10)),
                ["keys"] = new JsonArray(Key("current", 2, Current, "active"), Key("previous", 1, Previous, "readable"))
            };
        }

        public ResolvedSecret Secret(string? payload = null, DateTimeOffset? resolvedAt = null) =>
            new(ModerationRecognitionKeyRing.SecretSettingKey, payload ?? Payload.ToJsonString(),
                SecretSourceType.EnvironmentVariable, SecretScope.Instance, null, resolvedAt ?? Clock.Now, Revision);

        public ModerationRecognitionKeyRing Parse(string? payload = null, DateTimeOffset? resolvedAt = null) =>
            ModerationRecognitionKeyRing.Parse(Secret(payload, resolvedAt), TrustDomainId, Clock);

        public static string Timestamp(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
        public static string Encode(byte[] material) => Convert.ToBase64String(material).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        public static JsonObject Key(string id, long generation, byte[] material, string status) =>
            new() { ["kid"] = id, ["generation"] = generation, ["k"] = Encode(material), ["status"] = status };

        public void Dispose()
        {
            CryptographicOperations.ZeroMemory(Current);
            CryptographicOperations.ZeroMemory(Previous);
        }
    }
}
