using Explore.Domain;
using Explore.Domain.Enums;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationRecognitionKeyLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task CandidateCannotWriteBeforeIndependentOverlapAcknowledgement()
    {
        var ring = Create();
        ring.Stage("next", 2, new string('b', 64), 2, Now);
        await Assert.That(() => ring.AuthorizeSwitch("next", 3, null, Guid.CreateVersion7(), Now))
            .Throws<InvalidOperationException>();
        await Assert.That(ring.Keys.Single(row => row.State == ModerationRecognitionKeyState.Writing).KeyId)
            .IsEqualTo("original");
    }

    [Test]
    public async Task OldRuntimeAcknowledgementCannotAuthorizeNewReplica()
    {
        var ring = Create();
        ring.Stage("next", 2, new string('b', 64), 2, Now);
        var replica = Guid.CreateVersion7();
        var acknowledgement = KeyReplica.Acknowledge(Guid.CreateVersion7(), replica, ring,
            "binding-revision", SecretSourceType.EnvironmentVariable, Now, Now.AddMinutes(1));
        await Assert.That(() => ring.AuthorizeSwitch("next", 3, acknowledgement, Guid.CreateVersion7(), Now))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task SameInstantAcknowledgementsHaveDeterministicDurableOrder()
    {
        var ring = Create();
        ring.Stage("next", 2, new string('b', 64), 2, Now);
        var replica = Guid.CreateVersion7();
        var first = KeyReplica.Acknowledge(Guid.CreateVersion7(), replica, ring, "selected",
            SecretSourceType.EnvironmentVariable, Now, Now.AddMinutes(1));
        var latest = KeyReplica.Acknowledge(Guid.CreateVersion7(), replica, ring, "selected",
            SecretSourceType.EnvironmentVariable, Now, Now.AddMinutes(1));
        await Assert.That(latest.Sequence).IsEqualTo(2L);
        await Assert.That(() => ring.AuthorizeSwitch("next", 3, first, replica, Now))
            .Throws<InvalidOperationException>();
        ring.AuthorizeSwitch("next", 3, latest, replica, Now);
        await Assert.That(ring.RingGeneration).IsEqualTo(3L);
    }

    [Test]
    public async Task AcknowledgedOverlapSwitchPreservesOriginalReadableCommitment()
    {
        var ring = Create();
        ring.Stage("next", 2, new string('b', 64), 2, Now);
        var replica = Guid.CreateVersion7();
        var acknowledgement = KeyReplica.Acknowledge(Guid.CreateVersion7(), replica, ring,
            "binding-revision", SecretSourceType.EnvironmentVariable, Now, Now.AddMinutes(1));
        ring.AuthorizeSwitch("next", 3, acknowledgement, replica, Now);
        await Assert.That(ring.Keys.Single(row => row.KeyId == "original").State)
            .IsEqualTo(ModerationRecognitionKeyState.Readable);
        await Assert.That(ring.Keys.Single(row => row.KeyId == "original").VerificationTag)
            .IsEqualTo(new string('a', 64));
        await Assert.That(ring.RingGeneration).IsEqualTo(3);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    public async Task RejectedAcknowledgementPreservesSequenceAndCurrentRing(int defect)
    {
        var ring = Create();
        var runtime = Guid.CreateVersion7();
        var original = (ring.Revision, ring.RingGeneration, ring.AcknowledgementSequence, ring.Commitment, ring.ChangedAt);
        Action acknowledge = () => KeyReplica.Acknowledge(
            defect == 0 ? Guid.NewGuid() : Guid.CreateVersion7(),
            defect == 1 ? Guid.Empty : runtime, ring, defect == 2 ? "" : "selected",
            defect == 3 ? (SecretSourceType)int.MaxValue : SecretSourceType.EnvironmentVariable,
            defect == 4 ? Now.AddTicks(-1) : defect == 7 ? Now.ToOffset(TimeSpan.FromHours(1)) : Now,
            defect == 5 ? Now : defect == 6 ? DateTimeOffset.MaxValue : Now.AddMinutes(1));
        if (defect < 2)
            await Assert.That(acknowledge).Throws<ArgumentException>();
        else
            await Assert.That(acknowledge).Throws<InvalidOperationException>();

        ring.ValidatePersistedState();
        await Assert.That((ring.Revision, ring.RingGeneration, ring.AcknowledgementSequence, ring.Commitment, ring.ChangedAt))
            .IsEqualTo(original);
        var accepted = KeyReplica.Acknowledge(Guid.CreateVersion7(), runtime, ring, "selected",
            SecretSourceType.EnvironmentVariable, Now, Now.AddMinutes(1));
        await Assert.That(accepted.Sequence).IsEqualTo(1L);
        await Assert.That(accepted.Matches(ring, runtime, Now)).IsTrue();
    }

    [Test]
    [Arguments(0, false)]
    [Arguments(1, false)]
    [Arguments(2, false)]
    [Arguments(3, false)]
    [Arguments(0, true)]
    [Arguments(1, true)]
    [Arguments(2, true)]
    public async Task ExhaustedEpochCannotPartiallyChangeKeyState(int operation, bool exhaustRing)
    {
        var ring = Create();
        var runtime = Guid.CreateVersion7();
        if (operation is 1 or 2)
            ring.Stage("next", 2, new string('b', 64), 2, Now);
        if (operation == 2)
        {
            var overlap = KeyReplica.Acknowledge(Guid.CreateVersion7(), runtime, ring, "selected",
                SecretSourceType.EnvironmentVariable, Now, Now.AddMinutes(1));
            ring.AuthorizeSwitch("next", 3, overlap, runtime, Now);
        }
        typeof(ModerationRecognitionKeyLifecycle).GetProperty(
            exhaustRing ? nameof(ring.RingGeneration) : nameof(ring.Revision))!
            .SetValue(ring, long.MaxValue - 1);
        var acknowledgement = operation is 1 or 2
            ? KeyReplica.Acknowledge(Guid.CreateVersion7(), runtime, ring, "selected",
                SecretSourceType.EnvironmentVariable, Now, Now.AddMinutes(1))
            : null;
        var original = (ring.Revision, ring.RingGeneration, ring.AcknowledgementSequence, ring.Commitment, ring.ChangedAt);
        Action transition = operation switch
        {
            0 => () => ring.Stage("next", 2, new string('b', 64), exhaustRing ? long.MaxValue : 2, Now),
            1 => () => ring.AuthorizeSwitch("next", exhaustRing ? long.MaxValue : 3, acknowledgement, runtime, Now),
            2 => () => ring.AuthorizeRetirement("original", exhaustRing ? long.MaxValue : 4,
                [], 0, 0, acknowledgement, runtime, Now),
            _ => () => ring.Compromise("original", Now)
        };
        await Assert.That(transition).Throws<InvalidOperationException>();
        ring.ValidatePersistedState();
        await Assert.That((ring.Revision, ring.RingGeneration, ring.AcknowledgementSequence, ring.Commitment, ring.ChangedAt))
            .IsEqualTo(original);
    }

    [Test]
    public async Task CompromisedGenerationCannotBeReplacedUnderTheSameIdentity()
    {
        var ring = Create();
        ring.Compromise("original", Now);
        await Assert.That(() => ring.Stage("original", 1, new string('b', 64), 2, Now))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task OriginalComputedObligationSurvivesMatchingExpiryAndBlocksBackupHorizonRetirement()
    {
        var (item, source, _, _) = Original();
        var obligation = KeyObligation.Capture(item, source, Receipt(item));
        obligation.BindAuthority(1);
        item.Drop(Now.AddDays(2));
        obligation.Prune(item, Now.AddDays(2));
        await Assert.That(obligation.ReadableUntil).IsEqualTo(Now.AddDays(40));
        await Assert.That(obligation.CanRetire(Now.AddDays(40).AddTicks(-1), 1)).IsFalse();
        await Assert.That(obligation.CanRetire(Now.AddDays(40), 0)).IsFalse();
        await Assert.That(obligation.CanRetire(Now.AddDays(40), 1)).IsTrue();
        await Assert.That(obligation.KeyId).IsEqualTo("original");
        await Assert.That(obligation.KeyGeneration).IsEqualTo(1L);
    }

    [Test]
    public async Task AliasRecaptureNeverRestartsOriginalReadableDeadline()
    {
        var (item, source, decision, sanction) = Original();
        var fresh = Guid.CreateVersion7();
        var binding = ModerationRecognitionContinuityBinding.Bind(Guid.CreateVersion7(), fresh,
            item, decision, sanction, Now);
        var alias = ModerationRecognitionCaptureItem.Recapture(Guid.CreateVersion7(), Guid.CreateVersion7(),
            fresh, item, binding, decision, sanction,
            new(item.TrustDomainId, AuthenticationProviderKind.Keycloak, item.Purpose,
                "candidate", 2, 1, new string('c', 64)), new string('d', 64), 2, Now.AddHours(1));
        var original = KeyObligation.Capture(item, source, Receipt(item));
        var recaptured = KeyObligation.Capture(alias, source, Receipt(alias));
        await Assert.That(recaptured.OriginalCapturedAt).IsEqualTo(Now);
        await Assert.That(recaptured.ReadableUntil).IsEqualTo(original.ReadableUntil);
        await Assert.That(recaptured.OriginalRetainUntil).IsEqualTo(item.RetainUntil);
    }

    [Test]
    public async Task CaptureObligationRequiresExactSourceAndImmutableAuthoritySequence()
    {
        var (item, source, _, _) = Original();
        var obligation = KeyObligation.Capture(item, source, Receipt(item));
        obligation.BindAuthority(1);
        await Assert.That(() => obligation.BindAuthority(2)).Throws<InvalidOperationException>();
        await Assert.That(() => obligation.Prune(item, Now.AddHours(1))).Throws<InvalidOperationException>();
        await Assert.That(obligation.Matches(item)).IsTrue();
    }

    [Test]
    public async Task RetirementRejectsOriginalObligationEvenWithCurrentReplicaAcknowledgement()
    {
        var (item, source, _, _) = Original();
        var obligation = KeyObligation.Capture(item, source, Receipt(item));
        obligation.BindAuthority(1);
        var lifecycle = ModerationRecognitionKeyLifecycle.Initialize(item.TrustDomainId, source.HostBindingId,
            item.KeyId, item.KeyGeneration, item.KeyVerificationTag, 1, Now, 0, 0);
        lifecycle.Stage("candidate", 2, new string('b', 64), 2, Now);
        var runtime = Guid.CreateVersion7();
        var overlap = KeyReplica.Acknowledge(Guid.CreateVersion7(), runtime, lifecycle, "selected",
            SecretSourceType.EnvironmentVariable, Now, Now.AddMinutes(1));
        lifecycle.AuthorizeSwitch("candidate", 3, overlap, runtime, Now);
        var instant = Now.AddDays(39);
        var ack = KeyReplica.Acknowledge(Guid.CreateVersion7(), runtime, lifecycle, "selected",
            SecretSourceType.EnvironmentVariable, instant, instant.AddMinutes(1));
        await Assert.That(() => lifecycle.AuthorizeRetirement("original", 4, [obligation], 1, 1, ack, runtime, instant))
            .Throws<InvalidOperationException>();
        instant = Now.AddDays(40);
        ack = KeyReplica.Acknowledge(Guid.CreateVersion7(), runtime, lifecycle, "selected",
            SecretSourceType.EnvironmentVariable, instant, instant.AddMinutes(1));
        lifecycle.AuthorizeRetirement("original", 4, [obligation], 1, 1, ack, runtime, instant);
        await Assert.That(lifecycle.Keys.Single(row => row.KeyId == "original").State)
            .IsEqualTo(ModerationRecognitionKeyState.Retired);
    }

    private static (ModerationRecognitionCaptureItem Item, ModerationRecognitionPrivacySource Source,
        ModerationDecision Decision, ModerationSanction Sanction) Original(TimeSpan? retention = null)
    {
        var instance = AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
        var scope = AuthorityScope.Tenant(instance, Guid.CreateVersion7());
        var privacy = Guid.CreateVersion7();
        var recovery = Guid.CreateVersion7();
        var governor = Guid.CreateVersion7();
        var source = ModerationRecognitionPrivacySource.Record(Guid.CreateVersion7(), Guid.CreateVersion7(),
            scope.TrustDomainId, scope.Id, Guid.CreateVersion7(), 1, Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), 1, new string('a', 64), ModerationRecognitionPrivacyTests.Terms(),
            new(privacy, privacy, recovery, recovery, governor, "Independent canonical controller review.",
                "Qualified privacy owner mandate for original recognition.",
                "Qualified recovery owner mandate for finite recovery."),
            ModerationRecognitionPrivacyTests.Retention(), governor, 3, Now, Now.AddDays(1));
        var decision = ModerationDecision.Propose(Guid.CreateVersion7(), Guid.CreateVersion7(),
            SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance),
            SubjectReference.Create(ModerationSubjectKind.Human, governor, instance), instance, scope,
            ModerationRuleReference.Create(scope.Id, "recognition.key-obligation", 1), [ModerationCapability.Publish],
            Now, Now.AddDays(2), null, 1, Now);
        decision.RecordResponse(1, Now);
        decision.AcceptFinding(Guid.CreateVersion7(), 2, Now);
        decision.Approve(SubjectReference.Create(ModerationSubjectKind.Human, privacy, instance), instance, 3, Now);
        var sanction = decision.Activate(Guid.CreateVersion7(), 4, Now);
        var purpose = ModerationReferenceRecognition.SeparatelyQualifiedFinalAction;
        var material = new ModerationRecognitionCapturedMaterial(
            new(scope.TrustDomainId, AuthenticationProviderKind.Keycloak, purpose, "original", 1, 1,
                new string('f', 64)), new string('a', 64),
            ModerationRecognitionContribution.Capture(decision, sanction, purpose, Now,
                Now.Add(retention ?? TimeSpan.FromDays(3))));
        return (ModerationRecognitionCaptureItem.Capture(Guid.CreateVersion7(), Guid.CreateVersion7(), decision,
            material, source.Id, 1, 3, Guid.CreateVersion7(), 1, 4, 1), source, decision, sanction);
    }

    private static ModerationRecognitionKeyLifecycle Create() =>
        ModerationRecognitionKeyLifecycle.Initialize(Guid.CreateVersion7(), Guid.CreateVersion7(),
            "original", 1, new string('a', 64), 1, Now, 0, 0);

    private static string Receipt(ModerationRecognitionCaptureItem item)
    {
        var pending = ModerationRecognitionPending.Receive(item.IntentId, item.ErasureSubjectId, 1,
            PrivacyErasureReasonCode.AccountDeletion, Guid.CreateVersion7(), Guid.CreateVersion7(), item.CapturedAt,
            item.CapturedAt.AddHours(1), item.CapturedAt.AddMinutes(5),
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32), item.CapturedAt.AddDays(1));
        pending.Capture([item], item.CapturedAt);
        return pending.MaterialReceipt!;
    }

    [Test]
    public async Task LinkedReceiptCeilingDoesNotExtendOrShortenTheUnlinkableOriginalKeyHorizon()
    {
        var (item, source, _, _) = Original(TimeSpan.FromDays(365));
        var obligation = KeyObligation.Capture(item, source, Receipt(item));
        obligation.BindAuthority(1);
        var lifecycle = ModerationRecognitionKeyLifecycle.Initialize(item.TrustDomainId, source.HostBindingId,
            item.KeyId, item.KeyGeneration, item.KeyVerificationTag, 1, Now, 0, 0);
        lifecycle.RecordObligation(obligation);
        item.Drop(Now.AddDays(2));
        obligation.Prune(item, Now.AddDays(2));
        obligation.Prune(item, Now.AddDays(3));
        await Assert.That(obligation.ReceiptRetainUntil).IsEqualTo(Now.AddDays(92));
        await Assert.That(obligation.ReadableUntil).IsEqualTo(Now.AddDays(402));
        lifecycle.Stage("candidate", 2, new string('b', 64), 2, Now.AddDays(92));
        var runtime = Guid.CreateVersion7();
        var overlap = KeyReplica.Acknowledge(Guid.CreateVersion7(), runtime, lifecycle, "selected",
            SecretSourceType.EnvironmentVariable, Now.AddDays(92), Now.AddDays(92).AddMinutes(1));
        lifecycle.AuthorizeSwitch("candidate", 3, overlap, runtime, Now.AddDays(92));
        var acknowledgement = KeyReplica.Acknowledge(Guid.CreateVersion7(), runtime, lifecycle, "selected",
            SecretSourceType.EnvironmentVariable, Now.AddDays(92), Now.AddDays(92).AddMinutes(1));
        // Privacy-linked rows are gone; the key retains no subject/intent identifier, only its
        // original finite horizon and required authority watermark.
        await Assert.That(() => lifecycle.AuthorizeRetirement("original", 4, [], 1, 1,
            acknowledgement, runtime, Now.AddDays(92))).Throws<InvalidOperationException>();
        var final = KeyReplica.Acknowledge(Guid.CreateVersion7(), runtime, lifecycle, "selected",
            SecretSourceType.EnvironmentVariable, Now.AddDays(402), Now.AddDays(402).AddMinutes(1));
        lifecycle.AuthorizeRetirement("original", 4, [], 1, 1, final, runtime, Now.AddDays(402));
        await Assert.That(lifecycle.Keys.Single(row => row.KeyId == "original").State)
            .IsEqualTo(ModerationRecognitionKeyState.Retired);
    }
}
