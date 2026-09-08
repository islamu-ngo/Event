using System.Reflection;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests.Entities;

public sealed class RegistrationAttemptRuntimeContractTests
{
    private static readonly DateTime CreatedAt = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ExpiresAt = CreatedAt.AddMinutes(15);

    [Test]
    public async Task Create_PinsRuntimeLineageAndStoresOnlyCapabilityHash()
    {
        RegistrationAttempt attempt = CreateAttempt();

        await Assert.That(attempt.TenantId).IsNotEqualTo(Guid.Empty);
        await Assert.That(attempt.RegistrationOrderId).IsNotEqualTo(Guid.Empty);
        await Assert.That(attempt.RegistrationWorkflowId).IsNotEqualTo(Guid.Empty);
        await Assert.That(attempt.RegistrationRequirementId).IsNotEqualTo(Guid.Empty);
        await Assert.That(attempt.RegistrationChannelId).IsNotEqualTo(Guid.Empty);
        await Assert.That(attempt.RegistrationFormVersionId).IsNotEqualTo(Guid.Empty);
        await Assert.That(attempt.CapabilityTokenHash.Value).IsEqualTo(Hash(1).Value);
        await Assert.That(typeof(RegistrationAttempt).GetProperties().Select(property => property.Name))
            .DoesNotContain("CapabilityToken")
            .And.DoesNotContain("PlaintextCapability")
            .And.DoesNotContain("Token");
    }

    [Test]
    public async Task Consume_OnlyActiveUnexpiredAttempts_AndExpiryBoundaryIsExpired()
    {
        RegistrationAttempt consumed = CreateAttempt();
        RegistrationAttempt boundary = CreateAttempt();
        RegistrationAttempt expired = CreateAttempt();

        consumed.Consume(ExpiresAt.AddTicks(-1));

        await Assert.That(consumed.StatusId).IsEqualTo((int)RegistrationAttemptStatusEnum.Consumed);
        await Assert.That(consumed.ConsumedAt).IsEqualTo(ExpiresAt.AddTicks(-1));
        await Assert.That(() => consumed.Consume(ExpiresAt.AddTicks(-1))).Throws<InvalidOperationException>();
        await Assert.That(() => boundary.Consume(ExpiresAt)).Throws<InvalidOperationException>();

        expired.Expire(ExpiresAt);

        await Assert.That(expired.StatusId).IsEqualTo((int)RegistrationAttemptStatusEnum.Expired);
        await Assert.That(() => expired.Consume(ExpiresAt.AddTicks(1))).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task RuntimeStatusEnums_PinStableLookupIdentities()
    {
        await Assert.That((int)RegistrationAttemptStatusEnum.Active).IsEqualTo(1);
        await Assert.That((int)RegistrationAttemptStatusEnum.Consumed).IsEqualTo(2);
        await Assert.That((int)RegistrationAttemptStatusEnum.Expired).IsEqualTo(3);
        await Assert.That((int)RegistrationAttemptStatusEnum.Superseded).IsEqualTo(4);
        await Assert.That((int)RegistrationSubmissionStatusEnum.Received).IsEqualTo(1);
        await Assert.That((int)RegistrationSubmissionStatusEnum.Finalized).IsEqualTo(2);
        await Assert.That((int)RegistrationSubmissionStatusEnum.EvidenceOnly).IsEqualTo(3);
    }

    [Test]
    public async Task Create_RejectsMalformedLineageAndInvalidTimestamps()
    {
        await Assert.That(() => CreateAttemptWith(tenantId: Guid.Empty))
            .Throws<ArgumentException>();
        await Assert.That(() => CreateAttemptWith(expiresAt: CreatedAt))
            .Throws<ArgumentException>();
        await Assert.That(() => CreateAttemptWith(createdAt: DateTime.SpecifyKind(CreatedAt, DateTimeKind.Local)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Supersede_IsSingleUseAndBlocksConsumptionButNotEvidenceReceipt()
    {
        RegistrationAttempt attempt = CreateAttempt();
        Guid replacementAttemptId = Guid.CreateVersion7();
        DateTime supersededAt = CreatedAt.AddMinutes(3);

        attempt.Supersede(replacementAttemptId, supersededAt, "new form version");
        RegistrationSubmission lateSubmission = RegistrationSubmission.CreateNativeEvidenceOnly(
            attempt,
            EvidenceHash(2),
            supersededAt.AddMinutes(1),
            TransportHash(3));

        await Assert.That(attempt.StatusId).IsEqualTo((int)RegistrationAttemptStatusEnum.Superseded);
        await Assert.That(attempt.SupersededByRegistrationAttemptId).IsEqualTo(replacementAttemptId);
        await Assert.That(attempt.SupersessionReason).IsEqualTo("new form version");
        await Assert.That(() => attempt.Supersede(Guid.CreateVersion7(), supersededAt.AddSeconds(1), "again"))
            .Throws<InvalidOperationException>();
        await Assert.That(() => attempt.Consume(supersededAt.AddSeconds(1))).Throws<InvalidOperationException>();
        await Assert.That(lateSubmission.StatusId).IsEqualTo((int)RegistrationSubmissionStatusEnum.EvidenceOnly);
        await Assert.That(lateSubmission.IsFinalizable).IsFalse();
        await Assert.That(() => lateSubmission.Finalize(attempt, CreatedAt.AddMinutes(5))).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task SupersededProviderAttempt_RetainsLateCallbackEvidenceButNeverBecomesFinalizable()
    {
        RegistrationAttempt attempt = CreateAttempt(providerBindingId: Guid.CreateVersion7(), providerMappingRevisionHash: EvidenceHash(31));
        attempt.Supersede(Guid.CreateVersion7(), CreatedAt.AddMinutes(2), "restart-with-fallback");

        RegistrationSubmission lateCallback = RegistrationSubmission.CreateProviderEvidenceOnly(
            attempt,
            EvidenceHash(32),
            CreatedAt.AddMinutes(3),
            TransportHash(33),
            "provider-submission",
            "provider-revision",
            "subject",
            "correlation");

        await Assert.That(lateCallback.StatusId).IsEqualTo((int)RegistrationSubmissionStatusEnum.EvidenceOnly);
        await Assert.That(lateCallback.IsFinalizable).IsFalse();
        await Assert.That(() => attempt.SubmitProvider(
                EvidenceHash(34),
                CreatedAt.AddMinutes(4),
                TransportHash(35),
                "provider-submission-2",
                "provider-revision-2",
                null,
                null))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Submission_DedupIdentityIgnoresHttpIdempotencyAndInitialEvidenceIsImmutable()
    {
        RegistrationAttempt attempt = CreateAttempt();
        RegistrationSubmission first = attempt.SubmitNative(
            EvidenceHash(4),
            CreatedAt.AddMinutes(1),
            TransportHash(5));
        RegistrationSubmission replayWithDifferentHttpKey = RegistrationSubmission.CreateNativeEvidenceOnly(
            attempt,
            EvidenceHash(4),
            CreatedAt.AddMinutes(2),
            TransportHash(6));

        first.Finalize(attempt, CreatedAt.AddMinutes(3));

        await Assert.That(first.BusinessDeduplicationKey).IsEqualTo(replayWithDifferentHttpKey.BusinessDeduplicationKey);
        await Assert.That(first.HttpIdempotencyKeyHash).IsNotEqualTo(replayWithDifferentHttpKey.HttpIdempotencyKeyHash);
        await Assert.That(first.ReceivedEvidenceHash).IsEqualTo(EvidenceHash(4));
        await Assert.That(first.StatusId).IsEqualTo((int)RegistrationSubmissionStatusEnum.Finalized);
        await Assert.That(first.FinalizedAt).IsEqualTo(CreatedAt.AddMinutes(3));
        await Assert.That(typeof(RegistrationSubmission).GetProperties().Select(property => property.Name))
            .DoesNotContain("Payload")
            .And.DoesNotContain("RawPayload")
            .And.DoesNotContain("AnswersJson");
    }

    [Test]
    public async Task SubmitNative_ConsumesAttemptAtomicallyAndSecondReceiptIsEvidenceOnly()
    {
        RegistrationAttempt attempt = CreateAttempt();

        RegistrationSubmission accepted = attempt.SubmitNative(
            EvidenceHash(11),
            CreatedAt.AddMinutes(1),
            TransportHash(12));
        RegistrationSubmission lateReplay = RegistrationSubmission.CreateNativeEvidenceOnly(
            attempt,
            EvidenceHash(13),
            CreatedAt.AddMinutes(2),
            TransportHash(14));

        await Assert.That(attempt.StatusId).IsEqualTo((int)RegistrationAttemptStatusEnum.Consumed);
        await Assert.That(accepted.IsFinalizable).IsTrue();
        await Assert.That(lateReplay.IsFinalizable).IsFalse();
        await Assert.That(() => attempt.SubmitNative(EvidenceHash(15), CreatedAt.AddMinutes(3), null))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task BusinessDedupIdentity_DistinguishesNativePayloadsAttemptsAndProviderEvidence()
    {
        RegistrationAttempt firstNativeAttempt = CreateAttempt();
        RegistrationAttempt secondNativeAttempt = CreateAttempt();
        RegistrationAttempt providerAttempt = CreateAttempt(providerBindingId: Guid.CreateVersion7(), providerMappingRevisionHash: EvidenceHash(16));

        RegistrationSubmission nativeOne = RegistrationSubmission.CreateNativeEvidenceOnly(
            firstNativeAttempt,
            EvidenceHash(17),
            CreatedAt.AddMinutes(1),
            TransportHash(18));
        RegistrationSubmission nativeDifferentPayload = RegistrationSubmission.CreateNativeEvidenceOnly(
            firstNativeAttempt,
            EvidenceHash(19),
            CreatedAt.AddMinutes(1),
            TransportHash(18));
        RegistrationSubmission nativeDifferentAttempt = RegistrationSubmission.CreateNativeEvidenceOnly(
            secondNativeAttempt,
            EvidenceHash(17),
            CreatedAt.AddMinutes(1),
            TransportHash(18));
        RegistrationSubmission providerOne = RegistrationSubmission.CreateProviderEvidenceOnly(
            providerAttempt,
            EvidenceHash(20),
            CreatedAt.AddMinutes(1),
            TransportHash(18),
            "provider-submission",
            "provider-revision-1",
            "subject",
            "correlation");
        RegistrationSubmission providerRevision = RegistrationSubmission.CreateProviderEvidenceOnly(
            providerAttempt,
            EvidenceHash(21),
            CreatedAt.AddMinutes(1),
            TransportHash(18),
            "provider-submission",
            "provider-revision-2",
            "subject",
            "correlation");

        await Assert.That(nativeOne.BusinessDeduplicationKey).IsNotEqualTo(nativeDifferentPayload.BusinessDeduplicationKey);
        await Assert.That(nativeOne.BusinessDeduplicationKey).IsNotEqualTo(nativeDifferentAttempt.BusinessDeduplicationKey);
        await Assert.That(nativeOne.BusinessDeduplicationKey).IsNotEqualTo(providerOne.BusinessDeduplicationKey);
        await Assert.That(providerOne.BusinessDeduplicationKey).IsNotEqualTo(providerRevision.BusinessDeduplicationKey);
        await Assert.That(nativeOne.HttpIdempotencyKeyHash).IsEqualTo(nativeDifferentPayload.HttpIdempotencyKeyHash);
    }

    [Test]
    public async Task Finalize_RequiresCurrentConsumptionFenceAndRejectsStaleSupersededSnapshot()
    {
        RegistrationAttempt consumedAttempt = CreateAttempt();
        RegistrationSubmission accepted = consumedAttempt.SubmitNative(EvidenceHash(22), CreatedAt.AddMinutes(1), null);
        RegistrationAttempt staleAttempt = CreateAttempt();
        RegistrationSubmission staleSubmission = RegistrationSubmission.CreateNativeEvidenceOnly(
            staleAttempt,
            EvidenceHash(23),
            CreatedAt.AddMinutes(1),
            null);

        staleAttempt.Supersede(Guid.CreateVersion7(), CreatedAt.AddMinutes(2), "new attempt");
        accepted.Finalize(consumedAttempt, CreatedAt.AddMinutes(3));

        await Assert.That(accepted.StatusId).IsEqualTo((int)RegistrationSubmissionStatusEnum.Finalized);
        await Assert.That(() => staleSubmission.Finalize(staleAttempt, CreatedAt.AddMinutes(3)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task TemporalBounds_RejectTransitionsBeforeLineageCreation()
    {
        RegistrationAttempt attempt = CreateAttempt();

        await Assert.That(() => attempt.Consume(CreatedAt.AddTicks(-1))).Throws<ArgumentException>();
        await Assert.That(() => attempt.Supersede(Guid.CreateVersion7(), CreatedAt.AddTicks(-1), "old"))
            .Throws<ArgumentException>();
        await Assert.That(() => RegistrationSubmission.CreateNativeEvidenceOnly(
                attempt,
                EvidenceHash(24),
                CreatedAt.AddTicks(-1),
                null))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Revisions_RejectAfterFinalizedOrEvidenceOnlyAndMalformedProviderTuple()
    {
        RegistrationAttempt acceptedAttempt = CreateAttempt();
        RegistrationSubmission accepted = acceptedAttempt.SubmitNative(EvidenceHash(25), CreatedAt.AddMinutes(1), null);
        RegistrationAttempt supersededAttempt = CreateAttempt(providerBindingId: Guid.CreateVersion7(), providerMappingRevisionHash: EvidenceHash(26));
        supersededAttempt.Supersede(Guid.CreateVersion7(), CreatedAt.AddMinutes(1), "new attempt");
        RegistrationSubmission evidenceOnly = RegistrationSubmission.CreateProviderEvidenceOnly(
            supersededAttempt,
            EvidenceHash(27),
            CreatedAt.AddMinutes(2),
            null,
            "provider-submission",
            "provider-revision",
            null,
            null);

        accepted.Finalize(acceptedAttempt, CreatedAt.AddMinutes(2));

        await Assert.That(() => accepted.AddRevision(EvidenceHash(28), CreatedAt.AddMinutes(3), "rev"))
            .Throws<InvalidOperationException>();
        await Assert.That(() => evidenceOnly.AddRevision(EvidenceHash(29), CreatedAt.AddMinutes(3), "rev"))
            .Throws<InvalidOperationException>();
        await Assert.That(() => RegistrationSubmission.CreateProviderEvidenceOnly(
                CreateAttempt(),
                EvidenceHash(30),
                CreatedAt.AddMinutes(1),
                null,
                "provider-submission",
                "provider-revision",
                null,
                null))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task AddRevision_AppendsOrderedImmutableEvidenceAndRejectsOutOfOrderReceipts()
    {
        RegistrationAttempt attempt = CreateAttempt();
        RegistrationSubmission submission = attempt.SubmitNative(
            EvidenceHash(7),
            CreatedAt.AddMinutes(1),
            httpIdempotencyKeyHash: null);

        RegistrationSubmissionRevision first = submission.AddRevision(EvidenceHash(8), CreatedAt.AddMinutes(2), "rev-1");
        RegistrationSubmissionRevision second = submission.AddRevision(EvidenceHash(9), CreatedAt.AddMinutes(3), "rev-2");

        await Assert.That(first.RevisionNumber).IsEqualTo(1);
        await Assert.That(second.RevisionNumber).IsEqualTo(2);
        await Assert.That(submission.Revisions.Select(revision => revision.RevisionNumber)).IsEquivalentTo([1, 2]);
        await Assert.That(first.ReceivedEvidenceHash).IsEqualTo(EvidenceHash(8));
        await Assert.That(() => submission.AddRevision(EvidenceHash(10), CreatedAt.AddMinutes(2), "stale"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Finalize_RemainsValidAfterGenericConcurrencyStampRotatesOnSave()
    {
        RegistrationAttempt attempt = CreateAttempt();
        RegistrationSubmission accepted = attempt.SubmitNative(EvidenceHash(31), CreatedAt.AddMinutes(1), null);
        Guid persistenceRotatedStamp = Guid.CreateVersion7();

        attempt.ConcurrencyStamp = persistenceRotatedStamp;
        accepted.Finalize(attempt, CreatedAt.AddMinutes(2));

        await Assert.That(accepted.StatusId).IsEqualTo((int)RegistrationSubmissionStatusEnum.Finalized);
        await Assert.That(attempt.ConcurrencyStamp).IsEqualTo(persistenceRotatedStamp);
    }

    [Test]
    public async Task InvalidAcceptedSubmissionArguments_DoNotMutateAttemptState()
    {
        RegistrationAttempt nativeAttempt = CreateAttempt();
        RegistrationAttempt providerAttempt = CreateAttempt(providerBindingId: Guid.CreateVersion7(), providerMappingRevisionHash: EvidenceHash(32));
        Guid nativeStamp = nativeAttempt.ConcurrencyStamp;
        Guid providerStamp = providerAttempt.ConcurrencyStamp;

        await Assert.That(() => nativeAttempt.SubmitNative(EvidenceHash(33), CreatedAt.AddTicks(-1), null))
            .Throws<ArgumentException>();
        await Assert.That(() => providerAttempt.SubmitProvider(
                EvidenceHash(34),
                CreatedAt.AddMinutes(1),
                null,
                string.Empty,
                "revision",
                null,
                null))
            .Throws<ArgumentException>();

        await Assert.That(nativeAttempt.StatusId).IsEqualTo((int)RegistrationAttemptStatusEnum.Active);
        await Assert.That(nativeAttempt.ConsumedAt).IsNull();
        await Assert.That(nativeAttempt.ConcurrencyStamp).IsEqualTo(nativeStamp);
        await Assert.That(providerAttempt.StatusId).IsEqualTo((int)RegistrationAttemptStatusEnum.Active);
        await Assert.That(providerAttempt.ConsumedAt).IsNull();
        await Assert.That(providerAttempt.ConcurrencyStamp).IsEqualTo(providerStamp);
    }

    [Test]
    public async Task AttemptChannelKind_RejectsNativeProviderCrossoverForAcceptedAndEvidenceOnlyPaths()
    {
        RegistrationAttempt nativeAttempt = CreateAttempt();
        RegistrationAttempt providerAttempt = CreateAttempt(providerBindingId: Guid.CreateVersion7(), providerMappingRevisionHash: EvidenceHash(35));

        await Assert.That(() => providerAttempt.SubmitNative(EvidenceHash(36), CreatedAt.AddMinutes(1), null))
            .Throws<InvalidOperationException>();
        await Assert.That(() => RegistrationSubmission.CreateNativeEvidenceOnly(
                providerAttempt,
                EvidenceHash(37),
                CreatedAt.AddMinutes(1),
                null))
            .Throws<ArgumentException>();
        await Assert.That(() => nativeAttempt.SubmitProvider(
                EvidenceHash(38),
                CreatedAt.AddMinutes(1),
                null,
                "provider-submission",
                "revision",
                null,
                null))
            .Throws<ArgumentException>();
        await Assert.That(() => RegistrationSubmission.CreateProviderEvidenceOnly(
                nativeAttempt,
                EvidenceHash(39),
                CreatedAt.AddMinutes(1),
                null,
                "provider-submission",
                "revision",
                null,
                null))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ProviderBusinessKey_IsOpaqueDelimiterSafeAndIgnoresMappingRevision()
    {
        Guid bindingId = Guid.CreateVersion7();
        RegistrationAttempt firstMapping = CreateAttempt(providerBindingId: bindingId, providerMappingRevisionHash: EvidenceHash(40));
        RegistrationAttempt secondMapping = CreateAttempt(providerBindingId: bindingId, providerMappingRevisionHash: EvidenceHash(41));
        secondMapping.TenantId = firstMapping.TenantId;
        RegistrationSubmission first = RegistrationSubmission.CreateProviderEvidenceOnly(
            firstMapping,
            EvidenceHash(42),
            CreatedAt.AddMinutes(1),
            null,
            "provider:submission",
            "rev:1",
            null,
            null);
        RegistrationSubmission sameExternalResponseDifferentMapping = RegistrationSubmission.CreateProviderEvidenceOnly(
            secondMapping,
            EvidenceHash(43),
            CreatedAt.AddMinutes(1),
            null,
            "provider:submission",
            "rev:1",
            null,
            null);
        RegistrationSubmission delimiterVariant = RegistrationSubmission.CreateProviderEvidenceOnly(
            firstMapping,
            EvidenceHash(44),
            CreatedAt.AddMinutes(1),
            null,
            "provider",
            "submission:rev:1",
            null,
            null);

        await Assert.That(first.BusinessDeduplicationKey).StartsWith("sha256:");
        await Assert.That(first.BusinessDeduplicationKey).DoesNotContain("provider:submission");
        await Assert.That(first.BusinessDeduplicationKey).DoesNotContain("rev:1");
        await Assert.That(first.BusinessDeduplicationKey).IsEqualTo(sameExternalResponseDifferentMapping.BusinessDeduplicationKey);
        await Assert.That(first.BusinessDeduplicationKey).IsNotEqualTo(delimiterVariant.BusinessDeduplicationKey);
    }

    [Test]
    public async Task HashValueObjects_DoNotExposeHashValuesThroughToString()
    {
        RegistrationEvidenceHash evidence = EvidenceHash(45);
        RegistrationTransportIdempotencyHash transport = TransportHash(46);

        await Assert.That(evidence.ToString()).DoesNotContain(evidence.Value);
        await Assert.That(transport.ToString()).DoesNotContain(transport.Value);
        await Assert.That(evidence.ToString()).IsEqualTo("RegistrationEvidenceHash(<redacted>)");
        await Assert.That(transport.ToString()).IsEqualTo("RegistrationTransportIdempotencyHash(<redacted>)");
    }

    private static RegistrationAttempt CreateAttempt(
        Guid? providerBindingId = null,
        RegistrationEvidenceHash? providerMappingRevisionHash = null) => RegistrationAttempt.Create(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Hash(1),
        providerBindingId,
        providerMappingRevisionHash,
        CreatedAt,
        ExpiresAt);

    private static RegistrationAttempt CreateAttemptWith(
        Guid? tenantId = null,
        DateTime? createdAt = null,
        DateTime? expiresAt = null) => RegistrationAttempt.Create(
        tenantId ?? Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Hash(1),
        null,
        null,
        createdAt ?? CreatedAt,
        expiresAt ?? ExpiresAt);

    private static CapabilityTokenHash Hash(byte fill) => CapabilityTokenHash.Create(Convert.ToBase64String(Enumerable.Repeat(fill, 32).Select(value => (byte)value).ToArray()));

    private static RegistrationEvidenceHash EvidenceHash(byte fill) => RegistrationEvidenceHash.Create(Convert.ToBase64String(Enumerable.Repeat(fill, 32).Select(value => (byte)value).ToArray()));

    private static RegistrationTransportIdempotencyHash TransportHash(byte fill) => RegistrationTransportIdempotencyHash.Create(Convert.ToBase64String(Enumerable.Repeat(fill, 32).Select(value => (byte)value).ToArray()));
}
