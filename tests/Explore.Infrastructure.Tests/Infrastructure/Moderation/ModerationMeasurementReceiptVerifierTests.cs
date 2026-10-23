using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Infrastructure.Services.Moderation;
using ISLAMU.Moderation.Domain;

namespace Explore.Infrastructure.Tests.Infrastructure.Moderation;

public sealed class ModerationMeasurementReceiptVerifierTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ValidSignatureReturnsOnlyAuthenticReceiptAndEarliestDeadline()
    {
        using var fixture = new Fixture();
        var result = fixture.Verify();

        await Assert.That(result.Outcome).IsEqualTo(ModerationMeasurementVerificationOutcome.Known);
        await Assert.That(result.FailureReason).IsEqualTo(ModerationMeasurementVerificationFailure.None);
        await Assert.That(result.Receipt!.ReceiptId).IsEqualTo(fixture.ReceiptId);
        await Assert.That(result.Receipt.Binding).IsEqualTo(fixture.Binding);
        await Assert.That(result.Receipt.InstallationRevision).IsEqualTo(long.MaxValue);
        await Assert.That(result.Receipt.CaseIds.SequenceEqual(["case.one", "case.two"])).IsTrue();
        await Assert.That(result.ValidUntil).IsEqualTo(ObservedAt.AddMinutes(5));

        var shorterEnrollment = fixture.Trust with { ValidUntil = ObservedAt.AddMinutes(1) };
        var bounded = fixture.Verify(trust: shorterEnrollment);
        await Assert.That(bounded.Outcome).IsEqualTo(ModerationMeasurementVerificationOutcome.Known);
        await Assert.That(bounded.ValidUntil).IsEqualTo(shorterEnrollment.ValidUntil);
    }

    [Test]
    public async Task ForgedSignatureCannotAuthenticate()
    {
        using var fixture = new Fixture();
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await Rejected(fixture.Verify(token: fixture.Sign(key: attacker)),
            ModerationMeasurementVerificationFailure.InvalidSignature);
    }

    [Test]
    [Arguments("alg", "none")]
    [Arguments("alg", "HS256")]
    [Arguments("alg", "ES384")]
    [Arguments("typ", "JWT")]
    [Arguments("kid", "foreign")]
    [Arguments("jku", "https://attacker.invalid/keys")]
    [Arguments("x5u", "https://attacker.invalid/cert")]
    [Arguments("jwk", "untrusted")]
    [Arguments("crit", "unknown")]
    [Arguments("extra", "unknown")]
    public async Task HeaderMustUseOnlyPinnedProtocol(string field, string value)
    {
        using var fixture = new Fixture();
        fixture.Header[field] = value;
        await Rejected(fixture.Verify());
    }

    [Test]
    public async Task DifferentCanonicalKeyIdentifierCannotSelectAnotherKey()
    {
        using var fixture = new Fixture();
        fixture.Header["kid"] = Guid.CreateVersion7().ToString("D");
        await Rejected(fixture.Verify());
    }

    [Test]
    [Arguments("iss")]
    [Arguments("aud")]
    [Arguments("hostBindingId")]
    [Arguments("trustDomainId")]
    [Arguments("tenantId")]
    [Arguments("runnerDigest")]
    [Arguments("catalogueDigest")]
    [Arguments("testBundleDigest")]
    public async Task EvenSignedClaimsCannotReplaceEnrollmentFacts(string field)
    {
        using var fixture = new Fixture();
        fixture.Payload[field] = field.EndsWith("Id", StringComparison.Ordinal)
            ? Guid.CreateVersion7().ToString("D")
            : field.EndsWith("Digest", StringComparison.Ordinal) ? new string('b', 64) : "other";
        await Rejected(fixture.Verify());
    }

    [Test]
    public async Task ForeignBindingIsRejectedDespiteValidSignature()
    {
        using var fixture = new Fixture();
        var binding = fixture.Payload["binding"]!.AsObject();
        binding["policyRevision"] = "2";
        await Rejected(fixture.Verify(), ModerationMeasurementVerificationFailure.BindingMismatch);
    }

    [Test]
    public async Task ForeignJurisdictionCannotHideBehindMatchingTopLevelScope()
    {
        using var fixture = new Fixture();
        fixture.Payload["binding"]!["jurisdiction"]!["tenantId"] = Guid.CreateVersion7().ToString("D");
        await Rejected(fixture.Verify());
    }

    [Test]
    [Arguments("productArtifactDigest")]
    [Arguments("configurationDigest")]
    [Arguments("primaryConfigurationDigest")]
    [Arguments("testConfigurationDigest")]
    [Arguments("resultArtifactDigest")]
    [Arguments("runnerDigest")]
    [Arguments("catalogueDigest")]
    [Arguments("testBundleDigest")]
    [Arguments("consumerContractDigest")]
    [Arguments("inventoryFingerprint")]
    public async Task AlteringAnyBoundArtifactInvalidatesOriginalSignature(string field)
    {
        using var fixture = new Fixture();
        string original = fixture.Sign();
        fixture.Payload[field] = new string('b', 64);
        string[] parts = original.Split('.');
        string tampered = $"{parts[0]}.{Encode(Encoding.UTF8.GetBytes(fixture.Payload.ToJsonString()))}.{parts[2]}";
        await Rejected(fixture.Verify(token: tampered), ModerationMeasurementVerificationFailure.InvalidSignature);
    }

    [Test]
    public async Task AuthenticArtifactAssertionDoesNotEstablishNativeAcceptance()
    {
        using var fixture = new Fixture();
        fixture.Payload["productArtifactDigest"] = new string('b', 64);
        fixture.Payload["configurationDigest"] = new string('c', 64);
        fixture.Payload["resultArtifactDigest"] = new string('d', 64);
        var result = fixture.Verify();
        await Assert.That(result.Outcome).IsEqualTo(ModerationMeasurementVerificationOutcome.Known);
        await Assert.That(result.Receipt!.ProductArtifactDigest).IsEqualTo(new string('b', 64));
    }

    [Test]
    [Arguments("payload")]
    [Arguments("header")]
    [Arguments("binding")]
    [Arguments("jurisdiction")]
    [Arguments("rule")]
    public async Task UnknownAndDuplicateFieldsFailClosedAtEveryObject(string target)
    {
        using var fixture = new Fixture();
        JsonObject node = target switch
        {
            "header" => fixture.Header,
            "payload" => fixture.Payload,
            "binding" => fixture.Payload["binding"]!.AsObject(),
            "jurisdiction" => fixture.Payload["binding"]!["jurisdiction"]!.AsObject(),
            _ => fixture.Payload["binding"]!["rule"]!.AsObject()
        };
        node["unknown"] = true;
        await Rejected(fixture.Verify());
        node.Remove("unknown");
        var first = node.First();
        string text = node.ToJsonString();
        string duplicate = text.Insert(1, $"{JsonSerializer.Serialize(first.Key)}:{first.Value!.ToJsonString()},");
        string payload = fixture.Payload.ToJsonString();
        string header = fixture.Header.ToJsonString();
        if (target == "header") header = duplicate;
        else payload = payload.Replace(text, duplicate, StringComparison.Ordinal);
        await Rejected(fixture.Verify(token: fixture.Sign(payload, header)));
    }

    [Test]
    [Arguments("receiptId")]
    [Arguments("challengeId")]
    [Arguments("runId")]
    [Arguments("installationId")]
    public async Task MeasurementIdentitiesRequireCanonicalUuidVersionSeven(string field)
    {
        using var fixture = new Fixture();
        foreach (string invalid in new[] { Guid.Empty.ToString("D"), Guid.NewGuid().ToString("D"),
                     Guid.CreateVersion7().ToString("N"), "019A1234-ABCD-7ABC-8DEF-0123456789ABC" })
        {
            fixture.Payload[field] = invalid;
            await Rejected(fixture.Verify());
        }
    }

    [Test]
    [Arguments("0")]
    [Arguments("-1")]
    [Arguments("+1")]
    [Arguments("01")]
    [Arguments("1.0")]
    [Arguments("1e0")]
    [Arguments(" 1")]
    [Arguments("9223372036854775808")]
    public async Task RevisionsRequirePositiveCanonicalDecimalInt64Strings(string value)
    {
        using var fixture = new Fixture();
        fixture.Payload["installationRevision"] = value;
        await Rejected(fixture.Verify());
        fixture.Payload["installationRevision"] = "1";
        fixture.Payload["installationWriterGeneration"] = value;
        await Rejected(fixture.Verify());
        fixture.Payload["installationWriterGeneration"] = "1";
        fixture.Payload["binding"]!["policyRevision"] = value;
        await Rejected(fixture.Verify());
        fixture.Payload["binding"]!["policyRevision"] = "1";
        fixture.Payload["binding"]!["rule"]!["revision"] = value;
        await Rejected(fixture.Verify());
    }

    [Test]
    [Arguments("numeric-revision")]
    [Arguments("empty-cases")]
    [Arguments("duplicate-cases")]
    [Arguments("coverage-mismatch")]
    [Arguments("zero-executions")]
    [Arguments("zero-attempts")]
    [Arguments("fractional-count")]
    [Arguments("invalid-case")]
    [Arguments("null-field")]
    [Arguments("missing-field")]
    [Arguments("passed")]
    [Arguments("version")]
    [Arguments("audience-array")]
    [Arguments("uppercase-digest")]
    [Arguments("oversized-text")]
    [Arguments("missing-nonce")]
    [Arguments("malformed-nonce")]
    public async Task MalformedMeasurementCannotBecomeAuthenticReceipt(string mutation)
    {
        using var fixture = new Fixture();
        switch (mutation)
        {
            case "numeric-revision": fixture.Payload["installationRevision"] = 1; break;
            case "empty-cases": fixture.Payload["caseIds"] = new JsonArray(); break;
            case "duplicate-cases": fixture.Payload["caseIds"] = new JsonArray("case.one", "case.one"); break;
            case "coverage-mismatch": fixture.Payload["executedCount"] = 1; break;
            case "zero-executions": fixture.Payload["executedCount"] = 0; break;
            case "zero-attempts": fixture.Payload["attemptCount"] = 0; break;
            case "fractional-count": fixture.Payload["executedCount"] = 2.5; break;
            case "invalid-case": fixture.Payload["caseIds"] = new JsonArray("case.one", " case.two"); break;
            case "null-field": fixture.Payload["providerVersion"] = null; break;
            case "missing-field": fixture.Payload.Remove("resultArtifactDigest"); break;
            case "passed": fixture.Payload["passed"] = true; break;
            case "version": fixture.Payload["version"] = 2; break;
            case "audience-array": fixture.Payload["aud"] = new JsonArray("islamu.moderation.native-measurement"); break;
            case "uppercase-digest": fixture.Payload["resultArtifactDigest"] = new string('A', 64); break;
            case "oversized-text": fixture.Payload["providerVersion"] = new string('x', 257); break;
            case "missing-nonce": fixture.Payload.Remove("challengeNonce"); break;
            case "malformed-nonce": fixture.Payload["challengeNonce"] = "unbound"; break;
        }
        await Rejected(fixture.Verify());
    }

    [Test]
    [Arguments("expired")]
    [Arguments("future-issued")]
    [Arguments("regressing-completion")]
    [Arguments("regressing-issue")]
    [Arguments("non-utc")]
    [Arguments("infinite")]
    [Arguments("noncanonical")]
    public async Task InvalidReceiptWindowsHaveNoClockSkewAllowance(string mutation)
    {
        using var fixture = new Fixture();
        switch (mutation)
        {
            case "expired": fixture.Payload["validUntil"] = Instant(ObservedAt); break;
            case "future-issued": fixture.Payload["issuedAt"] = Instant(ObservedAt.AddTicks(1)); break;
            case "regressing-completion": fixture.Payload["completedAt"] = Instant(ObservedAt.AddMinutes(-3)); break;
            case "regressing-issue": fixture.Payload["issuedAt"] = Instant(ObservedAt.AddMinutes(-3)); break;
            case "non-utc": fixture.Payload["issuedAt"] = Instant(ObservedAt.ToOffset(TimeSpan.FromHours(1))); break;
            case "infinite": fixture.Payload["validUntil"] = Instant(DateTimeOffset.MaxValue); break;
            case "noncanonical": fixture.Payload["issuedAt"] = "2026-10-10T12:00:00Z"; break;
        }
        await Rejected(fixture.Verify());
    }

    [Test]
    public async Task EqualMeasurementInstantsAndOneTickRemainingAreValid()
    {
        using var fixture = new Fixture();
        foreach (string field in new[] { "startedAt", "completedAt", "issuedAt" })
            fixture.Payload[field] = Instant(ObservedAt);
        fixture.Payload["validUntil"] = Instant(ObservedAt.AddTicks(1));
        await Assert.That(fixture.Verify().Outcome).IsEqualTo(ModerationMeasurementVerificationOutcome.Known);
        await Rejected(fixture.Verify(observedAt: ObservedAt.AddTicks(1)));
        await Rejected(fixture.Verify(observedAt: ObservedAt.AddTicks(-1)));
        await Rejected(fixture.Verify(observedAt: ObservedAt.ToOffset(TimeSpan.FromHours(1))));
        await Rejected(fixture.Verify(observedAt: DateTimeOffset.MinValue));
        await Rejected(fixture.Verify(observedAt: DateTimeOffset.MaxValue));
    }

    [Test]
    public async Task MissingRetiredExpiredAndInvalidEnrollmentKeysFailClosed()
    {
        using var fixture = new Fixture();
        var verifier = new ModerationMeasurementReceiptVerifier();
        await Rejected(verifier.Verify(fixture.Sign(), null, fixture.Binding, ObservedAt),
            ModerationMeasurementVerificationFailure.UnknownKey);
        await Rejected(fixture.Verify(trust: fixture.Trust with { ValidUntil = ObservedAt }));
        await Rejected(fixture.Verify(trust: fixture.Trust with { ValidUntil = DateTimeOffset.MaxValue }));
        await Rejected(fixture.Verify(trust: fixture.Trust with
        {
            ValidUntil = fixture.Trust.ValidUntil.ToOffset(TimeSpan.FromHours(1))
        }));
        await Rejected(fixture.Verify(trust: fixture.Trust with { SubjectPublicKeyInfo = [1, 2, 3] }));
        await Rejected(fixture.Verify(trust: fixture.Trust with
        {
            SubjectPublicKeyInfo = [.. fixture.Trust.SubjectPublicKeyInfo, 0]
        }));
        using var otherCurve = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        await Rejected(fixture.Verify(trust: fixture.Trust with
        {
            SubjectPublicKeyInfo = [.. otherCurve.ExportSubjectPublicKeyInfo()]
        }));
    }

    [Test]
    public async Task CompactEncodingIsBoundedAndCanonical()
    {
        using var fixture = new Fixture();
        string token = fixture.Sign();
        string[] parts = token.Split('.');
        foreach (string invalid in new[]
                 {
                     "", "a.b.c", token + ".extra", token + "=", " " + token,
                     $"{parts[0]}=.{parts[1]}.{parts[2]}",
                     new string('x', ModerationMeasurementReceiptVerifier.MaximumTokenLength + 1)
                 })
            await Rejected(fixture.Verify(token: invalid));
        var verifier = new ModerationMeasurementReceiptVerifier();
        await Rejected(verifier.Verify(null, fixture.Trust, fixture.Binding, ObservedAt));
    }

    [Test]
    public async Task JsonRoundTripPreservesNativeBindingExactCasesAndInt64Revisions()
    {
        using var fixture = new Fixture();
        var receipt = JsonSerializer.Deserialize<ModerationMeasurementReceipt>(fixture.Payload.ToJsonString())!;
        var restored = JsonSerializer.Deserialize<ModerationMeasurementReceipt>(JsonSerializer.Serialize(receipt))!;
        await Assert.That(restored.Binding).IsEqualTo(fixture.Binding);
        await Assert.That(restored.InstallationRevision).IsEqualTo(long.MaxValue);
        await Assert.That(restored.CaseIds.SequenceEqual(["case.one", "case.two"])).IsTrue();
        var changed = restored with { CaseIds = restored.CaseIds.SetItem(0, "replacement") };
        await Assert.That(restored.CaseIds[0]).IsEqualTo("case.one");
        await Assert.That(changed.CaseIds[0]).IsEqualTo("replacement");
        await Assert.That(fixture.Verify(token: fixture.Sign(JsonSerializer.Serialize(restored))).Outcome)
            .IsEqualTo(ModerationMeasurementVerificationOutcome.Known);
    }

    [Test]
    public async Task SignedAlternateScalarSpellingsAndJsonLayoutsAreRejected()
    {
        using var fixture = new Fixture();
        string payload = fixture.Payload.ToJsonString();
        foreach (string alternate in new[]
                 {
                     " " + payload,
                     payload.Replace("\"version\":1", "\"version\":1.0", StringComparison.Ordinal),
                     payload.Replace("\"executedCount\":2", "\"executedCount\":2e0", StringComparison.Ordinal),
                     payload.Replace("\"consumerId\"", "\"consumer\\u0049d\"", StringComparison.Ordinal),
                     payload.Replace("case.one", "case.\\u006fne", StringComparison.Ordinal)
                 })
            await Rejected(fixture.Verify(token: fixture.Sign(alternate)));
    }

    private static async Task Rejected(ModerationMeasurementVerificationResult result,
        ModerationMeasurementVerificationFailure? reason = null)
    {
        await Assert.That(result.Outcome).IsEqualTo(ModerationMeasurementVerificationOutcome.Rejected);
        await Assert.That(result.Receipt).IsNull();
        await Assert.That(result.ValidUntil).IsNull();
        await Assert.That(result.FailureReason == ModerationMeasurementVerificationFailure.None).IsFalse();
        if (reason is { } expected)
            await Assert.That(result.FailureReason).IsEqualTo(expected);
    }

    private static string Instant(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class Fixture : IDisposable
    {
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        public Guid ReceiptId { get; } = Guid.CreateVersion7();
        public ModerationMeasurementTrustKey Trust { get; }
        public ModerationActivationBinding Binding { get; }
        public JsonObject Header { get; }
        public JsonObject Payload { get; }

        public Fixture()
        {
            var scope = AuthorityScope.Tenant(AuthorityScope.Instance(AuthorityScope.Solution(
                AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
                Guid.CreateVersion7()), Guid.CreateVersion7());
            Binding = new(Guid.CreateVersion7(), 1, Guid.CreateVersion7(), scope,
                ModerationRuleReference.Create(scope.Id, "publication", 1),
                ModerationCapability.Publish, ModerationNativeMode.Embedded);
            Trust = new()
            {
                Issuer = "native-test-launcher",
                KeyId = Guid.CreateVersion7(),
                SubjectPublicKeyInfo = _key.ExportSubjectPublicKeyInfo().ToImmutableArray(),
                HostBindingId = Guid.CreateVersion7(),
                TrustDomainId = scope.TrustDomainId,
                TenantId = scope.Id,
                ApprovedRunnerDigest = new string('a', 64),
                ApprovedCatalogueDigest = new string('a', 64),
                ApprovedTestBundleDigest = new string('a', 64),
                ValidUntil = ObservedAt.AddMinutes(10)
            };
            Header = new JsonObject
            {
                ["alg"] = "ES256", ["typ"] = "islamu-moderation-conformance+jwt", ["kid"] = Trust.KeyId.ToString("D")
            };
            var coordinates = scope.Coordinates;
            Payload = JsonSerializer.SerializeToNode(new
            {
                version = 1, iss = Trust.Issuer, aud = "islamu.moderation.native-measurement",
                receiptId = ReceiptId, challengeId = Guid.CreateVersion7(),
                challengeNonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)), runId = Guid.CreateVersion7(),
                hostBindingId = Trust.HostBindingId, trustDomainId = Trust.TrustDomainId, tenantId = Trust.TenantId,
                binding = new
                {
                    policyId = Binding.PolicyId, policyRevision = "1", publicationId = Binding.PublicationId,
                    jurisdiction = new
                    {
                        kind = "Tenant", trustDomainId = coordinates.TrustDomainId,
                        solutionId = coordinates.SolutionId, instanceId = coordinates.InstanceId,
                        tenantId = coordinates.TenantId, communityId = coordinates.CommunityId, resourceId = coordinates.ResourceId
                    },
                    rule = new { ownerId = scope.Id, code = "publication", revision = "1" },
                    capability = "Publish", mode = "Embedded"
                },
                consumerId = "event.publish", consumerContractDigest = new string('a', 64),
                installationId = Guid.CreateVersion7(), installationRevision = long.MaxValue.ToString(CultureInfo.InvariantCulture),
                installationWriterGeneration = "1", runtimeGeneration = "runtime-1", inventoryFingerprint = new string('a', 64),
                runnerDigest = Trust.ApprovedRunnerDigest, catalogueDigest = Trust.ApprovedCatalogueDigest,
                testBundleDigest = Trust.ApprovedTestBundleDigest, productArtifactDigest = new string('a', 64),
                configurationDigest = new string('a', 64), providerCode = "postgresql", providerVersion = "18.0",
                primaryConfigurationDigest = new string('1', 64), testConfigurationDigest = new string('2', 64),
                caseIds = new[] { "case.one", "case.two" }, executedCount = 2, attemptCount = 1,
                resultArtifactDigest = new string('a', 64),
                startedAt = Instant(ObservedAt.AddMinutes(-2)), completedAt = Instant(ObservedAt.AddMinutes(-1)),
                issuedAt = Instant(ObservedAt), validUntil = Instant(ObservedAt.AddMinutes(5))
            })!.AsObject();
        }

        public string Sign(string? payload = null, string? header = null, ECDsa? key = null)
        {
            string input = $"{Encode(Encoding.UTF8.GetBytes(header ?? Header.ToJsonString()))}." +
                Encode(Encoding.UTF8.GetBytes(payload ?? Payload.ToJsonString()));
            byte[] signature = (key ?? _key).SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            return $"{input}.{Encode(signature)}";
        }

        public ModerationMeasurementVerificationResult Verify(string? token = null,
            ModerationMeasurementTrustKey? trust = null, DateTimeOffset? observedAt = null) =>
            new ModerationMeasurementReceiptVerifier().Verify(token ?? Sign(), trust ?? Trust, Binding, observedAt ?? ObservedAt);

        public void Dispose() => _key.Dispose();
    }
}
