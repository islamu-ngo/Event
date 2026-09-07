using System.Security.Cryptography;
using System.Text.Json;
using Explore.Domain;

namespace Event.Domain.UnitTests;

public sealed class PrivacyErasureContractTests
{
    private static readonly DateTime RequestedAtUtc =
        new(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task PrivacyErasureIntent_ValidUserFactIsImmutableAndTyped()
    {
        PrivacyErasureIntent intent = CreateIntent(1);

        await Assert.That(intent.IntentId.Version).IsEqualTo(7);
        await Assert.That(intent.AuthoritySequence).IsEqualTo(1);
        await Assert.That(intent.SubjectKind).IsEqualTo(PrivacyErasureSubjectKind.User);
        await Assert.That(intent.SubjectId).IsNotEqualTo(Guid.Empty);
        await Assert.That(intent.ReasonCode).IsEqualTo(PrivacyErasureReasonCode.AccountDeletion);
        await Assert.That(intent.PolicyVersion).IsEqualTo(1);
        await Assert.That(intent.RequestedAtUtc).IsEqualTo(RequestedAtUtc);
        await Assert.That(intent.RecordedAtUtc).IsEqualTo(RequestedAtUtc.AddSeconds(1));
        await Assert.That(intent.RetentionExpiresAtUtc)
            .IsGreaterThan(intent.RecordedAtUtc);
        await Assert.That(typeof(PrivacyErasureIntent).GetProperties()
            .Any(property => property.SetMethod?.IsPublic == true)).IsFalse();
    }

    [Test]
    public async Task PrivacyErasureIntent_DefaultRetentionRemainsInfinityForHistoricalFacts()
    {
        PrivacyErasureIntent intent = CreateIntent(1);

        await Assert.That(intent.RetentionExpiresAtUtc)
            .IsEqualTo(DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc));
    }

    [Test]
    public async Task PrivacyErasureIntent_LegalHoldPseudonymizationPreservesAuthorityEvidence()
    {
        PrivacyErasureIntent intent = CreateIntent(17, policyVersion: 3);
        Guid originalSubjectId = intent.SubjectId;
        Guid intentAuditToken = Guid.NewGuid();
        Guid subjectAuditToken = Guid.NewGuid();

        intent.PseudonymizeForLegalHold(intentAuditToken, subjectAuditToken);

        await Assert.That(intent.IntentId).IsEqualTo(intentAuditToken);
        await Assert.That(intent.SubjectId).IsEqualTo(subjectAuditToken);
        await Assert.That(intent.SubjectId).IsNotEqualTo(originalSubjectId);
        await Assert.That(intent.IsLegalHoldPseudonymized).IsTrue();
        await Assert.That(intent.AuthoritySequence).IsEqualTo(17);
        await Assert.That(intent.PolicyVersion).IsEqualTo(3);
        await Assert.That(() => intent.PseudonymizeForLegalHold(Guid.Empty, subjectAuditToken))
            .Throws<ArgumentException>();
        await Assert.That(() => intent.PseudonymizeForLegalHold(intentAuditToken, Guid.Empty))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task PrivacyErasureIntent_RejectsMalformedIdentityKindReasonSequenceAndPolicy()
    {
        await Assert.That(() => CreateIntent(1, intentId: Guid.Empty)).Throws<ArgumentException>();
        await Assert.That(() => CreateIntent(1, intentId: Guid.NewGuid())).Throws<ArgumentException>();
        await Assert.That(() => CreateIntent(1, subjectKind: default)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => CreateIntent(1, subjectKind: (PrivacyErasureSubjectKind)2))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => CreateIntent(1, subjectId: Guid.Empty)).Throws<ArgumentException>();
        await Assert.That(() => CreateIntent(1, reasonCode: default)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => CreateIntent(1, reasonCode: (PrivacyErasureReasonCode)999))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => CreateIntent(0)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => CreateIntent(-1)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => CreateIntent(1, policyVersion: 0)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => CreateIntent(1, policyVersion: -1)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task PrivacyErasureIntent_RejectsDefaultNonUtcAndReversedTimestamps()
    {
        await Assert.That(() => PrivacyErasureIntent.Record(
                Guid.CreateVersion7(),
                1,
                PrivacyErasureSubjectKind.User,
                Guid.CreateVersion7(),
                PrivacyErasureReasonCode.AccountDeletion,
                1,
                default,
                RequestedAtUtc))
            .Throws<ArgumentException>();
        await Assert.That(() => CreateIntent(
                1,
                requestedAtUtc: DateTime.SpecifyKind(RequestedAtUtc, DateTimeKind.Unspecified)))
            .Throws<ArgumentException>();
        await Assert.That(() => CreateIntent(
                1,
                recordedAtUtc: DateTime.SpecifyKind(RequestedAtUtc, DateTimeKind.Local)))
            .Throws<ArgumentException>();
        await Assert.That(() => CreateIntent(
                1,
                requestedAtUtc: RequestedAtUtc.AddSeconds(2),
                recordedAtUtc: RequestedAtUtc.AddSeconds(1)))
            .Throws<ArgumentException>();
        await Assert.That(() => CreateIntent(
                1,
                retentionExpiresAtUtc: RequestedAtUtc))
            .Throws<ArgumentException>();
        await Assert.That(() => CreateIntent(
                1,
                retentionExpiresAtUtc: DateTime.SpecifyKind(
                    RequestedAtUtc.AddYears(1),
                    DateTimeKind.Unspecified)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task PrivacyErasureCounter_AllocatesOnlyContiguousPositiveSequences()
    {
        PrivacyErasureCounter counter = PrivacyErasureCounter.Start();

        await Assert.That(counter.Singleton).IsTrue();
        await Assert.That(counter.AllocateNext()).IsEqualTo(1);
        counter.AdvanceTo(2);
        await Assert.That(counter.LastSequence).IsEqualTo(2);
        InvalidOperationException? duplicate = await Assert.That(() => counter.AdvanceTo(2))
            .Throws<InvalidOperationException>();
        await Assert.That(duplicate!.Message).Contains("advance the ledger by one");
        await Assert.That(() => counter.AdvanceTo(4)).Throws<InvalidOperationException>();

        typeof(PrivacyErasureCounter).GetProperty(nameof(PrivacyErasureCounter.LastSequence))!
            .SetValue(counter, long.MaxValue);
        InvalidOperationException? exhausted = await Assert.That(() => counter.AllocateNext())
            .Throws<InvalidOperationException>();
        await Assert.That(exhausted!.Message).Contains("sequence is exhausted");
    }

    [Test]
    public async Task PrivacyErasureAuthorityState_RejectsNegativeOrInvertedWatermarks()
    {
        var state = new PrivacyErasureAuthorityState(50, 20);
        var compactedToHighWater = new PrivacyErasureAuthorityState(50, 50);

        await Assert.That(state.HighWaterSequence).IsEqualTo(50);
        await Assert.That(state.RetainedFloorSequence).IsEqualTo(20);
        await Assert.That(compactedToHighWater.RetainedFloorSequence).IsEqualTo(50);
        await Assert.That(() => new PrivacyErasureAuthorityState(-1, 0))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new PrivacyErasureAuthorityState(50, -1))
            .Throws<ArgumentOutOfRangeException>();
        ArgumentException? inverted = await Assert.That(() =>
                new PrivacyErasureAuthorityState(50, 51))
            .Throws<ArgumentException>();
        await Assert.That(inverted!.Message).Contains("retained floor");
    }

    [Test]
    public async Task PrivacyErasureCounter_AdvancesRetainedFloorMonotonicallyWithinHighWater()
    {
        PrivacyErasureCounter counter = PrivacyErasureCounter.Start();
        counter.AdvanceTo(1);
        counter.AdvanceTo(2);
        counter.AdvanceRetainedFloorTo(1);

        await Assert.That(counter.GetState())
            .IsEqualTo(new PrivacyErasureAuthorityState(2, 1));
        counter.AdvanceRetainedFloorTo(1);
        counter.AdvanceRetainedFloorTo(2);
        await Assert.That(counter.GetState())
            .IsEqualTo(new PrivacyErasureAuthorityState(2, 2));
        InvalidOperationException? rollback = await Assert.That(() =>
                counter.AdvanceRetainedFloorTo(0))
            .Throws<InvalidOperationException>();
        await Assert.That(rollback!.Message).Contains("advance monotonically");
        await Assert.That(() => counter.AdvanceRetainedFloorTo(3))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task PrivacyErasureReplayCheckpoint_RejectsDuplicateGapAndMismatchedFacts()
    {
        PrivacyErasureIntent firstIntent = CreateIntent(1);
        PrivacyErasureIntent secondIntent = CreateIntent(2);
        PrivacyErasureReplayCheckpoint first = PrivacyErasureReplayCheckpoint.Start(
            firstIntent,
            RequestedAtUtc.AddMinutes(1),
            Guid.CreateVersion7());
        PrivacyErasureReplayCheckpoint second = PrivacyErasureReplayCheckpoint.Advance(
            first,
            secondIntent,
            RequestedAtUtc.AddMinutes(2),
            Guid.CreateVersion7());

        await Assert.That(second.Matches(secondIntent)).IsTrue();
        await Assert.That(second.Matches(CreateIntent(2, policyVersion: 2))).IsFalse();
        await Assert.That(() => PrivacyErasureReplayCheckpoint.Advance(
                second,
                secondIntent,
                RequestedAtUtc.AddMinutes(3)))
            .Throws<InvalidOperationException>();
        await Assert.That(() => PrivacyErasureReplayCheckpoint.Advance(
                second,
                CreateIntent(4),
                RequestedAtUtc.AddMinutes(3)))
            .Throws<InvalidOperationException>();
        await Assert.That(() => PrivacyErasureReplayCheckpoint.Start(
                CreateIntent(2),
                RequestedAtUtc.AddMinutes(1)))
            .Throws<InvalidOperationException>();
        await Assert.That(() => PrivacyErasureReplayCheckpoint.Start(
                firstIntent,
                RequestedAtUtc.AddMinutes(1),
                Guid.NewGuid()))
            .Throws<ArgumentException>();
        await Assert.That(() => PrivacyErasureReplayCheckpoint.Start(
                firstIntent,
                default,
                Guid.CreateVersion7()))
            .Throws<ArgumentException>();
        await Assert.That(() => PrivacyErasureReplayCheckpoint.Start(
                firstIntent,
                DateTime.SpecifyKind(RequestedAtUtc.AddMinutes(1), DateTimeKind.Unspecified),
                Guid.CreateVersion7()))
            .Throws<ArgumentException>();
        await Assert.That(() => PrivacyErasureReplayCheckpoint.Start(
                firstIntent,
                RequestedAtUtc.AddSeconds(-1),
                Guid.CreateVersion7()))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task PrivacyErasureIntent_ValidFactSerializesToBoundedPersistedShape()
    {
        PrivacyErasureIntent intent = CreateIntent(1);
        string[] allowedProperties = typeof(PrivacyErasureIntent)
            .GetProperties()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] forbiddenProperties = ["Table", "Column", "Sql", "Json", "Metadata"];
        string serialized = JsonSerializer.Serialize(intent);
        using JsonDocument validDocument = JsonDocument.Parse(serialized);
        string[] serializedProperties = validDocument.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(serializedProperties).IsEquivalentTo(allowedProperties);
        await Assert.That(allowedProperties.Any(name => forbiddenProperties.Contains(
            name,
            StringComparer.OrdinalIgnoreCase))).IsFalse();
    }

    [Test]
    public async Task PrivacyErasureSagaAndCoverage_AreTypedHashedAndPolicyVersioned()
    {
        PrivacyErasureIntent intent = CreateIntent(1);
        DateTime fencedAtUtc = RequestedAtUtc.AddMinutes(1);
        byte[] receiptHash = new byte[32];
        PrivacyErasureSaga saga = PrivacyErasureSaga.Start(
            intent,
            1,
            receiptHash,
            fencedAtUtc.AddHours(24),
            fencedAtUtc,
            Guid.CreateVersion7());
        PrivacyErasurePolicyCoverage coverage = PrivacyErasurePolicyCoverage.Record(
            intent,
            2,
            fencedAtUtc.AddMinutes(1));

        await Assert.That(saga.SubjectKind).IsEqualTo(PrivacyErasureSubjectKind.User);
        await Assert.That(saga.ReceiptHash.Length).IsEqualTo(32);
        await Assert.That(saga.ReceiptExpiresAtUtc).IsGreaterThan(saga.FencedAtUtc);
        await Assert.That(coverage.IntentId).IsEqualTo(intent.IntentId);
        await Assert.That(coverage.SubjectKind).IsEqualTo(PrivacyErasureSubjectKind.User);
        await Assert.That(coverage.PolicyVersion).IsEqualTo(2);
        await Assert.That(() => PrivacyErasureSaga.Start(
                intent,
                0,
                receiptHash,
                fencedAtUtc.AddHours(24),
                fencedAtUtc))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => PrivacyErasureSaga.Start(
                intent,
                1,
                new byte[31],
                fencedAtUtc.AddHours(24),
                fencedAtUtc))
            .Throws<ArgumentException>();
        await Assert.That(() => PrivacyErasurePolicyCoverage.Record(
                intent,
                0,
                fencedAtUtc))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task PrivacyErasureSaga_EnforcesReceiptExpiryConcurrencyAndSettlement()
    {
        DateTime now = new(2026, 7, 23, 8, 0, 0, DateTimeKind.Utc);
        PrivacyErasureIntent intent = CreateIntent(1, recordedAtUtc: now);
        byte[] receiptHash = SHA256.HashData("receipt"u8);
        Guid concurrencyToken = Guid.CreateVersion7();
        PrivacyErasureSaga saga = PrivacyErasureSaga.Start(
            intent,
            intent.AuthoritySequence,
            receiptHash,
            now.AddHours(1),
            now,
            concurrencyToken);

        await Assert.That(saga.Status).IsEqualTo(PrivacyErasureSagaStatus.Fenced);
        await Assert.That(saga.Authenticates(receiptHash, now.AddMinutes(59))).IsTrue();
        await Assert.That(saga.Authenticates(SHA256.HashData("wrong"u8), now.AddMinutes(1))).IsFalse();
        await Assert.That(saga.Authenticates(receiptHash, now.AddHours(1))).IsFalse();
        await Assert.That(() => saga.MarkLocalSettled(
                now.AddMinutes(1),
                1,
                Guid.CreateVersion7()))
            .Throws<InvalidOperationException>();

        saga.MarkLocalSettled(now.AddMinutes(1), 1, concurrencyToken);
        await Assert.That(saga.Status).IsEqualTo(PrivacyErasureSagaStatus.ProviderPending);
        await Assert.That(saga.LocalSettledAtUtc).IsEqualTo(now.AddMinutes(1));
    }

    [Test]
    public async Task PrivacyErasureProviderWork_FencesUnknownOutcomesAndReconciliation()
    {
        DateTime now = new(2026, 7, 23, 8, 0, 0, DateTimeKind.Utc);
        PrivacyErasureIntent intent = CreateIntent(1, recordedAtUtc: now);
        Guid leaseToken = Guid.CreateVersion7();
        PrivacyErasureProviderWork work = PrivacyErasureProviderWork.Create(
            Guid.CreateVersion7(),
            intent,
            PrivacyErasureProviderKind.Keycloak,
            PrivacyErasureProviderAction.DeletePlatformManagedIdentity,
            null,
            null,
            PrivacyErasureProviderLocatorKind.AccountIdentifier,
            "protected-provider-key",
            1,
            now.AddDays(7),
            now);

        await Assert.That(work.ProtectedLocator).IsEqualTo("protected-provider-key");

        PrivacyErasureProviderClaim claim = work.Claim("worker", leaseToken, now, now.AddMinutes(1));
        work.MarkUnknown(claim.FenceToken, leaseToken, now.AddSeconds(1), "ambiguous_acknowledgement");

        await Assert.That(work.Status).IsEqualTo(PrivacyErasureProviderWorkStatus.Unknown);
        await Assert.That(() => work.MarkSucceeded(
                claim.FenceToken - 1,
                leaseToken,
                now.AddSeconds(2)))
            .Throws<InvalidOperationException>();

        work.Reconcile(PrivacyErasureProviderReconciliation.Completed, now.AddMinutes(2));
        await Assert.That(work.Status).IsEqualTo(PrivacyErasureProviderWorkStatus.Completed);
        await Assert.That(work.CompletedAtUtc).IsEqualTo(now.AddMinutes(2));
        await Assert.That(work.ProtectedLocator).IsNull();
    }

    [Test]
    public async Task PrivacyErasureSaga_ClearsExpiredReceiptHashWithoutChangingExpiredAuthenticationOutcome()
    {
        DateTime now = new(2026, 7, 25, 8, 0, 0, DateTimeKind.Utc);
        var receiptHash = new byte[32];
        receiptHash[0] = 7;
        PrivacyErasureSaga saga = PrivacyErasureSaga.Start(
            CreateIntent(1, recordedAtUtc: now),
            1,
            receiptHash,
            now.AddHours(1),
            now);

        await Assert.That(saga.Authenticates(receiptHash, now.AddHours(1))).IsFalse();
        saga.ClearExpiredReceiptHash(now.AddHours(1));

        await Assert.That(saga.ReceiptHash).IsNull();
        await Assert.That(saga.Authenticates(receiptHash, now.AddHours(1))).IsFalse();
        await Assert.That(saga.Authenticates(new byte[32], now.AddHours(1))).IsFalse();
    }

    [Test]
    public async Task PrivacyErasureProviderWork_ExpiresLocatorIntoBoundedDeadLetterState()
    {
        DateTime now = new(2026, 7, 23, 8, 0, 0, DateTimeKind.Utc);
        PrivacyErasureProviderWork work = PrivacyErasureProviderWork.Create(
            Guid.CreateVersion7(),
            CreateIntent(1, recordedAtUtc: now),
            PrivacyErasureProviderKind.WebPush,
            PrivacyErasureProviderAction.InvalidateSubscription,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            PrivacyErasureProviderLocatorKind.WebPushEndpoint,
            "protected-endpoint",
            1,
            now.AddDays(7),
            now);

        await Assert.That(() => work.ExpireLocator(now.AddDays(7).AddTicks(-1)))
            .Throws<InvalidOperationException>();

        work.ExpireLocator(now.AddDays(7));

        await Assert.That(work.ProtectedLocator).IsNull();
        await Assert.That(work.Status).IsEqualTo(PrivacyErasureProviderWorkStatus.DeadLettered);
        await Assert.That(work.LastFailureCode).IsEqualTo("locator_expired");
        await Assert.That(work.DeadLetteredAtUtc).IsEqualTo(now.AddDays(7));
    }

    private static PrivacyErasureIntent CreateIntent(
        long authoritySequence,
        Guid? intentId = null,
        PrivacyErasureSubjectKind subjectKind = PrivacyErasureSubjectKind.User,
        Guid? subjectId = null,
        PrivacyErasureReasonCode reasonCode = PrivacyErasureReasonCode.AccountDeletion,
        int policyVersion = 1,
        DateTime? requestedAtUtc = null,
        DateTime? recordedAtUtc = null,
        DateTime? retentionExpiresAtUtc = null) =>
        PrivacyErasureIntent.Record(
            intentId ?? Guid.CreateVersion7(),
            authoritySequence,
            subjectKind,
            subjectId ?? Guid.CreateVersion7(),
            reasonCode,
            policyVersion,
            requestedAtUtc ?? RequestedAtUtc,
            recordedAtUtc ?? RequestedAtUtc.AddSeconds(1),
            retentionExpiresAtUtc);
}
